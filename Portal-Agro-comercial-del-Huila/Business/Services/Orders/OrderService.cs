using Business.Interfaces.Implements.Notification;
using Business.Interfaces.Implements.Orders;
using Business.Interfaces.Implements.Producers.Cloudinary;
using Data.Interfaces.Implements.Auth;
using Data.Interfaces.Implements.Orders;
using Data.Interfaces.Implements.Producers;
using Data.Interfaces.Implements.Producers.Products;
using Entity.Domain.Enums;
using Entity.Domain.Models.Implements.Orders;
using Entity.Domain.Models.Implements.Producers;
using Entity.Domain.Models.Implements.Producers.Products;
using Entity.DTOs.Notifications;
using Entity.DTOs.Order.Create;
using Entity.DTOs.Order.Select;
using Entity.Infrastructure.Context;
using Mapster;
using MapsterMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Utilities.Custom.Code;
using Utilities.Exceptions;
using Utilities.Messaging.Interfaces;

namespace Business.Services.Orders
{
    public class OrderService : IOrderService
    {
        private readonly IMapper _mapper;
        private readonly ILogger<OrderService> _logger;
        private readonly IOrderRepository _orderRepository;
        private readonly ICloudinaryService _cloudinaryService;
        private readonly IProductRepository _productRepository;
        private readonly IProducerRepository _producerRepository;
        private readonly IOrderEmailService _orderEmailService;
        private readonly IUserRepository _userRepository;
        private readonly ApplicationDbContext _db;
        private readonly int _paymentUploadDeadlineHours;
        private readonly int _deliveredConfirmDeadlineHours;
        private readonly INotificationService _notifications;

        public OrderService(
            IMapper mapper,
            ILogger<OrderService> logger,
            IOrderRepository orderRepository,
            INotificationService notification,
            ICloudinaryService cloudinaryService,
            IProductRepository productRepository,
            ApplicationDbContext db,
            IOrderEmailService orderEmailService,
            IUserRepository userRepository,
            IProducerRepository producerRepository,
            IConfiguration cfg)
        {
            _mapper = mapper;
            _logger = logger;
            _notifications = notification;
            _orderRepository = orderRepository;
            _cloudinaryService = cloudinaryService;
            _productRepository = productRepository;
            _orderEmailService = orderEmailService;
            _userRepository = userRepository;
            _db = db;
            _producerRepository = producerRepository;

            var hours = cfg.GetValue<int>("Orders:PaymentUploadDeadlineHours", 24);
            _paymentUploadDeadlineHours = Math.Clamp(hours, 1, 168);
            var hours2 = cfg.GetValue<int>("Orders:DeliveredConfirmDeadlineHours", 48);
            _deliveredConfirmDeadlineHours = Math.Clamp(hours2, 1, 336);
        }

        public async Task<int> CreateOrderAsync(int userId, OrderCreateDto dto)
        {
            NormalizeCreateDto(dto);
            ValidateCreateDto(dto);

            var product = await GetAvailableProductAsync(dto.ProductId);

            if (product.Producer != null && product.Producer.UserId == userId)
                throw new BusinessException("No puedes comprar tus propios productos.");

            if (product.Stock < dto.QuantityRequested)
                throw new BusinessException("Stock insuficiente para la cantidad solicitada.");

            var now = DateTime.UtcNow;
            var order = BuildOrderEntity(userId, dto, product, now);

            await _orderRepository.AddAsync(order);
            await _db.SaveChangesAsync();

            await SendOrderCreatedEmailsSafelyAsync(order);

            // FIX: Notificar al productor usando su UserId (no ProducerId)
            var producerUserId = await GetProducerUserIdAsync(order.ProducerIdSnapshot);

            await _notifications.CreateAsync(new CreateNotificationRequest
            {
                UserId = producerUserId, // FIX
                Title = "Nuevo pedido recibido",
                Message = $"Tienes un nuevo pedido {order.Id} por {order.QuantityRequested} unidad(es) de {order.ProductNameSnapshot}.",
                RelatedType = "Order",
                RelatedRoute = $"/account/producer/orders/{order.Code}"
            });

            await _notifications.CreateAsync(new CreateNotificationRequest
            {
                UserId = order.UserId,
                Title = "Pedido creado",
                Message = $"Tu pedido {order.Id} fue registrado y está pendiente de revisión del productor.",
                RelatedType = "Order",
                RelatedRoute = $"/account/orders/{order.Code}"
            });

            return order.Id;
        }

        public async Task AcceptOrderAsync(int userId, string code, OrderAcceptDto dto)
        {
            var producerId = await _producerRepository.GetIdProducer(userId)
                             ?? throw new BusinessException("El usuario no está registrado como productor.");

            var order = await _orderRepository.GetByCode(code)
                       ?? throw new BusinessException("Orden no encontrada.");

            if (order.IsDeleted || !order.Active)
                throw new BusinessException("La orden no está disponible.");
            if (order.ProducerIdSnapshot != producerId)
                throw new BusinessException("No está autorizado para aceptar esta orden.");
            if (order.Status != OrderStatus.PendingReview)
                throw new BusinessException("Solo se pueden aceptar órdenes en estado pendiente.");

            order.RowVersion = Convert.FromBase64String(dto.RowVersion);

            var now = DateTime.UtcNow;
            order.ProducerNotes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();
            order.ProducerDecisionAt = now;
            order.Status = OrderStatus.AcceptedAwaitingPayment;
            order.AcceptedAt = now;
            order.AutoCloseAt = now.AddHours(_paymentUploadDeadlineHours);

            var strategy = _db.Database.CreateExecutionStrategy();

            await strategy.ExecuteAsync(async () =>
            {
                await using var tx = await _db.Database.BeginTransactionAsync();
                try
                {
                    var ok = await _productRepository.TryDecrementStockAsync(order.ProductId, order.QuantityRequested);
                    if (!ok)
                        throw new BusinessException("Stock insuficiente o concurrencia detectada. Refresca e inténtalo de nuevo.");

                    await _orderRepository.UpdateOrderAsync(order);
                    await _db.SaveChangesAsync();

                    await tx.CommitAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    await tx.RollbackAsync();
                    throw new BusinessException("La orden fue modificada por otro usuario. Refresca y vuelve a intentar.");
                }
                catch
                {
                    await tx.RollbackAsync();
                    throw;
                }
            });

            var user = await _userRepository.GetContactUser(order.UserId)
                      ?? throw new BusinessException("No se pudo obtener el contacto del usuario.");

            await _orderEmailService.SendOrderAcceptedAwaitingPaymentToCustomer(
                emailReceptor: user.Email,
                orderId: order.Id,
                productName: order.ProductNameSnapshot,
                quantityRequested: order.QuantityRequested,
                total: order.Total,
                acceptedAtUtc: order.AcceptedAt!.Value,
                paymentDeadlineUtc: order.AutoCloseAt!.Value
            );

            await _notifications.CreateAsync(new CreateNotificationRequest
            {
                UserId = order.UserId,
                Title = "Pedido aceptado",
                Message = $"Tu pedido {order.Id} fue aceptado. Sube tu comprobante antes de la fecha límite.",
                RelatedType = "Order",
                RelatedRoute = $"/account/orders/{order.Code}"
            });
        }

        public async Task UploadPaymentAsync(int userId, string code, OrderUploadPaymentDto dto)
        {
            var order = await _orderRepository.GetByCode(code)
                       ?? throw new BusinessException("Orden no encontrada.");

            if (order.IsDeleted || !order.Active)
                throw new BusinessException("La orden no está disponible.");
            if (order.UserId != userId)
                throw new BusinessException("No está autorizado para subir el comprobante de esta orden.");
            if (order.Status != OrderStatus.AcceptedAwaitingPayment)
                throw new BusinessException("Solo se puede subir comprobante cuando la orden está aceptada y pendiente de pago.");
            if (order.AutoCloseAt.HasValue && DateTime.UtcNow > order.AutoCloseAt.Value)
                throw new BusinessException("El plazo para subir el comprobante expiró.");
            if (dto.PaymentImage == null || dto.PaymentImage.Length == 0)
                throw new BusinessException("Debes adjuntar el comprobante de pago.");

            order.RowVersion = Convert.FromBase64String(dto.RowVersion);

            string? uploadedPublicId = null;
            string? uploadedUrl = null;
            var now = DateTime.UtcNow;

            try
            {
                var upload = await _cloudinaryService.UploadOrderPaymentImageAsync(dto.PaymentImage, order.Id);
                uploadedPublicId = upload?.PublicId;
                uploadedUrl = upload?.SecureUrl?.AbsoluteUri
                              ?? throw new BusinessException("No se pudo obtener la URL del comprobante.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Fallo al subir comprobante (OrderId {OrderId})", order.Id);
                throw new BusinessException("No se pudo subir el comprobante. Intenta nuevamente.");
            }

            var strategy = _db.Database.CreateExecutionStrategy();
            try
            {
                await strategy.ExecuteAsync(async () =>
                {
                    await using var tx = await _db.Database.BeginTransactionAsync();
                    try
                    {
                        order.PaymentImageUrl = uploadedUrl;
                        order.PaymentUploadedAt = now;
                        order.PaymentSubmittedAt = now;

                        order.Status = OrderStatus.PaymentSubmitted;
                        order.AutoCloseAt = null;

                        await _orderRepository.UpdateOrderAsync(order);
                        await _db.SaveChangesAsync();

                        await tx.CommitAsync();
                    }
                    catch
                    {
                        await tx.RollbackAsync();
                        throw;
                    }
                });
            }
            catch (DbUpdateConcurrencyException)
            {
                await DeleteUploadedReceiptIfNeededAsync(uploadedPublicId);
                throw new BusinessException("La orden fue modificada por otro usuario. Refresca y vuelve a intentar.");
            }
            catch
            {
                await DeleteUploadedReceiptIfNeededAsync(uploadedPublicId);
                throw;
            }

            try
            {
                var producer = await _producerRepository.GetContactProducer(order.ProducerIdSnapshot)
                               ?? throw new BusinessException("No se pudo obtener el contacto del productor.");

                await _orderEmailService.SendPaymentSubmittedToProducer(
                    emailReceptor: producer.Email,
                    orderId: order.Id,
                    productName: order.ProductNameSnapshot,
                    quantityRequested: order.QuantityRequested,
                    total: order.Total,
                    uploadedAtUtc: order.PaymentUploadedAt!.Value
                );

                // FIX: Notificación al productor por su UserId
                var producerUserId = await GetProducerUserIdAsync(order.ProducerIdSnapshot);
                await _notifications.CreateAsync(new CreateNotificationRequest
                {
                    UserId = producerUserId, // FIX
                    Title = "Pago enviado por el cliente",
                    Message = $"El pedido {order.Id} tiene comprobante cargado. Revisa y continúa el proceso.",
                    RelatedType = "Order",
                    RelatedRoute = $"/account/producer/orders/{order.Code}"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed sending 'payment submitted' notifications (OrderId {OrderId})", order.Id);
            }
        }

        public async Task MarkPreparingAsync(int userId, string code, string rowVersionBase64)
        {
            var producerId = await _producerRepository.GetIdProducer(userId)
                             ?? throw new BusinessException("El usuario no está registrado como productor.");

            var order = await _orderRepository.GetByCode(code)
                       ?? throw new BusinessException("Orden no encontrada.");

            if (order.IsDeleted || !order.Active)
                throw new BusinessException("La orden no está disponible.");

            if (order.ProducerIdSnapshot != producerId)
                throw new BusinessException("No está autorizado para actualizar esta orden.");

            if (order.Status != OrderStatus.PaymentSubmitted)
                throw new BusinessException("Solo se puede pasar a 'Preparando' desde 'Pago enviado'.");

            order.RowVersion = Convert.FromBase64String(rowVersionBase64);
            order.Status = OrderStatus.Preparing;

            try
            {
                await _orderRepository.UpdateOrderAsync(order);
                await _db.SaveChangesAsync();

                try
                {
                    var user = await _userRepository.GetContactUser(order.UserId)
                              ?? throw new BusinessException("No se pudo obtener el contacto del usuario.");

                    await _orderEmailService.SendOrderPreparingToCustomer(
                        emailReceptor: user.Email,
                        orderId: order.Id,
                        productName: order.ProductNameSnapshot,
                        quantityRequested: order.QuantityRequested,
                        total: order.Total,
                        preparingAtUtc: DateTime.UtcNow
                    );

                    await _notifications.CreateAsync(new CreateNotificationRequest
                    {
                        UserId = order.UserId,
                        Title = "Tu pedido está en preparación",
                        Message = $"El pedido {order.Code} pasó a estado 'Preparando'.",
                        RelatedType = "Order",
                        RelatedRoute = $"/account/orders/{order.Code}"
                    });
                }
                catch (Exception exMail)
                {
                    _logger.LogError(exMail, "Error enviando email 'preparación' (OrderId {OrderId})", order.Id);
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new BusinessException("La orden fue modificada por otro usuario. Refresca y vuelve a intentar.");
            }
        }

        public async Task MarkDispatchedAsync(int userId, string code, string rowVersionBase64)
        {
            var producerId = await _producerRepository.GetIdProducer(userId)
                             ?? throw new BusinessException("El usuario no está registrado como productor.");

            var order = await _orderRepository.GetByCode(code)
                       ?? throw new BusinessException("Orden no encontrada.");

            if (order.IsDeleted || !order.Active)
                throw new BusinessException("La orden no está disponible.");

            if (order.ProducerIdSnapshot != producerId)
                throw new BusinessException("No está autorizado para actualizar esta orden.");

            if (order.Status != OrderStatus.Preparing)
                throw new BusinessException("Solo se puede pasar a 'Despachado' desde 'Preparando'.");

            order.RowVersion = Convert.FromBase64String(rowVersionBase64);
            order.Status = OrderStatus.Dispatched;

            try
            {
                await _orderRepository.UpdateOrderAsync(order);
                await _db.SaveChangesAsync();

                try
                {
                    var user = await _userRepository.GetContactUser(order.UserId)
                              ?? throw new BusinessException("No se pudo obtener el contacto del usuario.");

                    await _orderEmailService.SendOrderDispatchedToCustomer(
                        emailReceptor: user.Email,
                        orderId: order.Id,
                        productName: order.ProductNameSnapshot,
                        quantityRequested: order.QuantityRequested,
                        total: order.Total,
                        dispatchedAtUtc: DateTime.UtcNow
                    );

                    await _notifications.CreateAsync(new CreateNotificationRequest
                    {
                        UserId = order.UserId,
                        Title = "Tu pedido fue despachado",
                        Message = $"El pedido {order.Id} fue despachado. Pronto lo recibirás.",
                        RelatedType = "Order",
                        RelatedRoute = $"/account/orders/{order.Code}"
                    });
                }
                catch (Exception exMail)
                {
                    _logger.LogError(exMail, "Error enviando email 'despachado' (OrderId {OrderId})", order.Id);
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new BusinessException("La orden fue modificada por otro usuario. Refresca y vuelve a intentar.");
            }
        }

        public async Task MarkDeliveredAsync(int userId, string code, string rowVersionBase64)
        {
            var producerId = await _producerRepository.GetIdProducer(userId)
                             ?? throw new BusinessException("El usuario no está registrado como productor.");

            var order = await _orderRepository.GetByCode(code)
                       ?? throw new BusinessException("Orden no encontrada.");

            if (order.IsDeleted || !order.Active)
                throw new BusinessException("La orden no está disponible.");

            if (order.ProducerIdSnapshot != producerId)
                throw new BusinessException("No está autorizado para actualizar esta orden.");

            if (order.Status != OrderStatus.Dispatched)
                throw new BusinessException("Solo se puede marcar 'Entregado (pendiente de confirmación)' desde 'Despachado'.");

            order.RowVersion = Convert.FromBase64String(rowVersionBase64);

            var now = DateTime.UtcNow;
            order.Status = OrderStatus.DeliveredPendingBuyerConfirm;
            order.UserConfirmEnabledAt = now;
            order.AutoCloseAt = now.AddHours(_deliveredConfirmDeadlineHours);

            try
            {
                await _orderRepository.UpdateOrderAsync(order);
                await _db.SaveChangesAsync();

                try
                {
                    var user = await _userRepository.GetContactUser(order.UserId)
                              ?? throw new BusinessException("No se pudo obtener el contacto del usuario.");

                    await _orderEmailService.SendOrderDeliveredToCustomer(
                        emailReceptor: user.Email,
                        orderId: order.Id,
                        productName: order.ProductNameSnapshot,
                        quantityRequested: order.QuantityRequested,
                        total: order.Total,
                        deliveredAtUtc: DateTime.UtcNow
                    );

                    await _notifications.CreateAsync(new CreateNotificationRequest
                    {
                        UserId = order.UserId,
                        Title = "Pedido entregado: confirma recepción",
                        Message = $"Marca si recibiste el pedido {order.Id}.",
                        RelatedType = "Order",
                        RelatedRoute = $"/account/orders/{order.Code}"
                    });
                }
                catch (Exception exMail)
                {
                    _logger.LogError(exMail, "Error enviando email 'entregado-pendiente-confirmación' (OrderId {OrderId})", order.Id);
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new BusinessException("La orden fue modificada por otro usuario. Refresca y vuelve a intentar.");
            }
        }

        public async Task CancelByUserAsync(int userId, string code, string rowVersionBase64)
        {
            var order = await _orderRepository.GetByCode(code)
                       ?? throw new BusinessException("Orden no encontrada.");

            if (order.UserId != userId) throw new BusinessException("No autorizado.");
            if (order.Status != OrderStatus.PendingReview)
                throw new BusinessException("Solo se puede cancelar antes de que el productor decida.");

            order.RowVersion = Convert.FromBase64String(rowVersionBase64);
            order.Status = OrderStatus.CancelledByUser;
            order.AutoCloseAt = null;

            try
            {
                await _orderRepository.UpdateOrderAsync(order);
                await _db.SaveChangesAsync();

                try
                {
                    var producer = await _producerRepository.GetContactProducer(order.ProducerIdSnapshot)
                                   ?? throw new BusinessException("No se pudo obtener el contacto del productor.");

                    await _orderEmailService.SendOrderCancelledByUserToProducer(
                        emailReceptor: producer.Email,
                        orderId: order.Id,
                        productName: order.ProductNameSnapshot,
                        quantityRequested: order.QuantityRequested,
                        cancelledAtUtc: DateTime.UtcNow
                    );

                    // FIX: Notificación al productor con su UserId
                    var producerUserId = await GetProducerUserIdAsync(order.ProducerIdSnapshot);
                    await _notifications.CreateAsync(new CreateNotificationRequest
                    {
                        UserId = producerUserId, // FIX
                        Title = "Pedido cancelado por el cliente",
                        Message = $"El pedido {order.Code} fue cancelado por el cliente.",
                        RelatedType = "Order",
                        RelatedRoute = $"/account/producer/orders/{order.Code}"
                    });
                }
                catch (Exception exMail)
                {
                    _logger.LogError(exMail, "Error enviando email 'cancelado por cliente' (OrderId {OrderId})", order.Id);
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new BusinessException("La orden fue modificada por otro usuario. Refresca y vuelve a intentar.");
            }
        }

        public async Task RejectOrderAsync(int userId, string code, OrderRejectDto dto)
        {
            var producerId = await _producerRepository.GetIdProducer(userId)
                             ?? throw new BusinessException("El usuario no está registrado como productor.");

            var order = await _orderRepository.GetByCode(code)
                       ?? throw new BusinessException("Orden no encontrada.");

            if (order.IsDeleted || !order.Active)
                throw new BusinessException("La orden no está disponible.");

            if (order.ProducerIdSnapshot != producerId)
                throw new BusinessException("No está autorizado para rechazar esta orden.");

            if (order.Status != OrderStatus.PendingReview)
                throw new BusinessException("Solo se pueden rechazar órdenes en estado pendiente.");

            order.RowVersion = Convert.FromBase64String(dto.RowVersion);

            order.ProducerDecisionAt = DateTime.UtcNow;
            order.ProducerDecisionReason = dto.Reason.Trim();
            order.Status = OrderStatus.Rejected;

            try
            {
                await _orderRepository.UpdateOrderAsync(order);
                await _db.SaveChangesAsync();

                var user = await _userRepository.GetContactUser(order.UserId)
                        ?? throw new BusinessException("No se pudo obtener el contacto del usuario.");

                await _orderEmailService.SendOrderRejectedToCustomer(
                    emailReceptor: user.Email,
                    orderId: order.Id,
                    productName: order.ProductNameSnapshot,
                    quantityRequested: order.QuantityRequested,
                    reason: order.ProducerDecisionReason!,
                    decisionAtUtc: order.ProducerDecisionAt!.Value
                );

                await _notifications.CreateAsync(new CreateNotificationRequest
                {
                    UserId = order.UserId,
                    Title = "Pedido rechazado",
                    Message = $"Tu pedido {order.Id} fue rechazado por el productor. Motivo: {order.ProducerDecisionReason}.",
                    RelatedType = "Order",
                    RelatedRoute = $"/account/orders/{order.Code}"
                });
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new BusinessException("La orden fue modificada por otro usuario. Refresca y vuelve a intentar.");
            }
        }

        public async Task ConfirmOrderAsync(int userId, string code, OrderConfirmDto dto)
        {
            var order = await _orderRepository.GetByCode(code)
                       ?? throw new BusinessException("Orden no encontrada.");

            if (order.IsDeleted || !order.Active)
                throw new BusinessException("La orden no está disponible.");

            if (order.UserId != userId)
                throw new BusinessException("No está autorizado para confirmar esta orden.");

            if (order.Status != OrderStatus.DeliveredPendingBuyerConfirm)
                throw new BusinessException("Solo se pueden confirmar órdenes que el productor marcó como entregadas.");

            var decisionAt = order.ProducerDecisionAt
                             ?? throw new BusinessException("Orden inválida: falta la fecha de decisión del productor.");

            order.RowVersion = Convert.FromBase64String(dto.RowVersion);

            var answer = dto.Answer.Trim().ToLowerInvariant();
            var now = DateTime.UtcNow;

            if (answer == "yes")
            {
                order.UserReceivedAnswer = UserReceivedAnswer.Yes;
                order.UserReceivedAt = now;
                order.Status = OrderStatus.Completed;
            }
            else if (answer == "no")
            {
                order.UserReceivedAnswer = UserReceivedAnswer.No;
                order.UserReceivedAt = now;
                order.Status = OrderStatus.Disputed;
            }
            else
            {
                throw new BusinessException("Answer debe ser 'Yes' o 'No'.");
            }

            try
            {
                await _orderRepository.UpdateOrderAsync(order);
                await _db.SaveChangesAsync();

                if (order.Status == OrderStatus.Completed)
                {
                    var producer = await _producerRepository.GetContactProducer(order.ProducerIdSnapshot)
                                   ?? throw new BusinessException("No se pudo obtener el contacto del productor.");

                    await _orderEmailService.SendOrderCompletedToProducer(
                        emailReceptor: producer.Email,
                        orderId: order.Id,
                        productName: order.ProductNameSnapshot,
                        quantityRequested: order.QuantityRequested,
                        total: order.Total,
                        completedAtUtc: order.UserReceivedAt!.Value
                    );

                    var user = await _userRepository.GetContactUser(order.UserId)
                              ?? throw new BusinessException("No se pudo obtener el contacto del usuario.");

                    await _orderEmailService.SendOrderCompletedToCustomer(
                        emailReceptor: user.Email,
                        orderId: order.Id,
                        productName: order.ProductNameSnapshot,
                        quantityRequested: order.QuantityRequested,
                        total: order.Total,
                        completedAtUtc: order.UserReceivedAt!.Value,
                        autoCompleted: false
                    );

                    // FIX: Notificación al productor con su UserId
                    var producerUserId = await GetProducerUserIdAsync(order.ProducerIdSnapshot);
                    await _notifications.CreateAsync(new CreateNotificationRequest
                    {
                        UserId = producerUserId, // FIX
                        Title = "Pedido completado",
                        Message = $"El cliente confirmó la recepción del pedido {order.Id}.",
                        RelatedType = "Order",
                        RelatedRoute = $"/account/producer/orders/{order.Code}"
                    });

                    await _notifications.CreateAsync(new CreateNotificationRequest
                    {
                        UserId = order.UserId,
                        Title = "¡Gracias! Pedido completado",
                        Message = $"Confirmaste la recepción del pedido.",
                        RelatedType = "Order",
                        RelatedRoute = $"/account/orders/{order.Code}"
                    });
                }
                else if (order.Status == OrderStatus.Disputed)
                {
                    var producer = await _producerRepository.GetContactProducer(order.ProducerIdSnapshot)
                                   ?? throw new BusinessException("No se pudo obtener el contacto del productor.");

                    await _orderEmailService.SendOrderDisputedToProducer(
                        emailReceptor: producer.Email,
                        orderId: order.Id,
                        productName: order.ProductNameSnapshot,
                        quantityRequested: order.QuantityRequested,
                        total: order.Total,
                        disputedAtUtc: order.UserReceivedAt!.Value
                    );

                    var producerUserId = await GetProducerUserIdAsync(order.ProducerIdSnapshot);

                    await _notifications.CreateAsync(new CreateNotificationRequest
                    {
                        UserId = producerUserId, // FIX
                        Title = "Pedido en disputa",
                        Message = $"El cliente marcó el pedido {order.Id} como 'No recibido'.",
                        RelatedType = "Order",
                        RelatedRoute = $"/account/producer/orders/{order.Code}"
                    });
                }
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new BusinessException("La orden fue modificada por otro usuario. Refresca y vuelve a intentar.");
            }
        }

        // ============== Helpers privados ==============

        private static void NormalizeCreateDto(OrderCreateDto dto)
        {
            dto.RecipientName = dto.RecipientName?.Trim() ?? string.Empty;
            dto.ContactPhone = dto.ContactPhone?.Trim() ?? string.Empty;
            dto.AddressLine1 = dto.AddressLine1?.Trim() ?? string.Empty;
            dto.AddressLine2 = string.IsNullOrWhiteSpace(dto.AddressLine2) ? null : dto.AddressLine2.Trim();
            dto.AdditionalNotes = string.IsNullOrWhiteSpace(dto.AdditionalNotes) ? null : dto.AdditionalNotes.Trim();
        }

        private static void ValidateCreateDto(OrderCreateDto dto)
        {
            if (dto is null) throw new ArgumentNullException(nameof(dto));
            if (dto.ProductId <= 0) throw new BusinessException("Producto inválido.");
            if (dto.QuantityRequested <= 0) throw new BusinessException("La cantidad solicitada debe ser mayor a cero.");
            if (string.IsNullOrWhiteSpace(dto.RecipientName)) throw new BusinessException("El nombre del destinatario es obligatorio.");
            if (string.IsNullOrWhiteSpace(dto.ContactPhone)) throw new BusinessException("El teléfono de contacto es obligatorio.");
            if (string.IsNullOrWhiteSpace(dto.AddressLine1)) throw new BusinessException("La dirección es obligatoria.");
            if (dto.CityId <= 0) throw new BusinessException("La ciudad es obligatoria.");
        }

        private async Task<Product> GetAvailableProductAsync(int productId)
        {
            var product = await _productRepository.GetByIdAsync(productId)
                          ?? throw new BusinessException("Producto no encontrado.");

            if (!product.Active || product.IsDeleted)
                throw new BusinessException("El producto no está disponible.");

            return product;
        }

        private static Order BuildOrderEntity(int userId, OrderCreateDto dto, Product product, DateTime now)
        {
            return new Order
            {
                UserId = userId,
                ProductId = product.Id,
                Code = CodeGenerator.Generate(),
                ProducerIdSnapshot = product.ProducerId,
                ProductNameSnapshot = product.Name,
                UnitPriceSnapshot = product.Price,
                QuantityRequested = dto.QuantityRequested,
                Subtotal = product.Price * dto.QuantityRequested,
                Total = product.Price * dto.QuantityRequested,
                Status = OrderStatus.PendingReview,
                RecipientName = dto.RecipientName,
                ContactPhone = dto.ContactPhone,
                AddressLine1 = dto.AddressLine1,
                AddressLine2 = dto.AddressLine2,
                CityId = dto.CityId,
                AdditionalNotes = dto.AdditionalNotes,
                CreateAt = now,
                Active = true,
                IsDeleted = false
            };
        }

        private async Task DeleteUploadedReceiptIfNeededAsync(string? uploadedPublicId)
        {
            if (!string.IsNullOrEmpty(uploadedPublicId))
            {
                try { await _cloudinaryService.DeleteAsync(uploadedPublicId); }
                catch { /* ignore */ }
            }
        }

        private async Task SendOrderCreatedEmailsSafelyAsync(Order order)
        {
            try
            {
                var producer = await _producerRepository.GetContactProducer(order.ProducerIdSnapshot)
                               ?? throw new BusinessException("No se pudo obtener el contacto del productor.");

                var user = await _userRepository.GetContactUser(order.UserId)
                           ?? throw new BusinessException("No se pudo obtener el contacto del usuario.");

                string producerName = $"{producer.FirstName?.Trim()} {producer.LastName?.Trim()}".Trim();
                if (string.IsNullOrWhiteSpace(producerName)) producerName = "Productor";

                string customerName = $"{user.FirstName?.Trim()} {user.LastName?.Trim()}".Trim();
                if (string.IsNullOrWhiteSpace(customerName)) customerName = "Cliente";

                await _orderEmailService.SendOrderCreatedEmail(
                    emailReceptor: producer.Email,
                    orderId: order.Id,
                    productName: order.ProductNameSnapshot,
                    quantityRequested: order.QuantityRequested,
                    subtotal: order.Subtotal,
                    total: order.Total,
                    createdAtUtc: order.CreateAt,
                    personName: producerName,
                    counterpartName: customerName,
                    isProducer: true
                );

                await _orderEmailService.SendOrderCreatedEmail(
                    emailReceptor: user.Email,
                    orderId: order.Id,
                    productName: order.ProductNameSnapshot,
                    quantityRequested: order.QuantityRequested,
                    subtotal: order.Subtotal,
                    total: order.Total,
                    createdAtUtc: order.CreateAt,
                    personName: customerName,
                    counterpartName: producerName,
                    isProducer: false
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed sending 'order created' emails (OrderId {OrderId})", order.Id);
            }
        }

        // Helper centralizado para obtener el UserId del productor
        // Si tu DTO de GetContactProducer no tiene UserId, reemplaza la lógica por un repo dedicado.
        private async Task<int> GetProducerUserIdAsync(int producerId)
        {
            var contact = await _producerRepository.GetContactProducer(producerId)
                          ?? throw new BusinessException("No se pudo obtener el contacto del productor.");
            if (contact.UserId == null) throw new BusinessException("El productor no tiene un UserId asociado.");
            else
            {
                var userId = contact.UserId.Value;
                return userId;
            }
        }
    }
}
