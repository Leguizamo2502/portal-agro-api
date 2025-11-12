using Business.Interfaces.Implements.Notification;                  
using Business.Services.BackgroundServices.Options;
using Data.Interfaces.Implements.Auth;
using Data.Interfaces.Implements.Producers;
using Entity.Domain.Enums;
using Entity.Domain.Models.Implements.Orders;
using Entity.DTOs.Notifications;                                     
using Entity.Infrastructure.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Utilities.Exceptions;
using Utilities.Messaging.Interfaces;

namespace Business.Services.BackgroundServices.Implements
{
    public sealed class ExpireAwaitingPaymentBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ExpireAwaitingPaymentBackgroundService> _logger;
        private readonly ExpireAwaitingPaymentJobOptions _opts;

        public ExpireAwaitingPaymentBackgroundService(
            IServiceScopeFactory scopeFactory,
            IOptions<ExpireAwaitingPaymentJobOptions> opts,
            ILogger<ExpireAwaitingPaymentBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _opts = opts.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var interval = TimeSpan.FromSeconds(Math.Max(30, _opts.ScanIntervalSeconds));
            var timer = new PeriodicTimer(interval);

            _logger.LogInformation("ExpireAwaitingPayment job iniciado. Intervalo={Interval}s, BatchSize={Batch}",
                _opts.ScanIntervalSeconds, _opts.BatchSize);

            try
            {
                while (!stoppingToken.IsCancellationRequested &&
                       await timer.WaitForNextTickAsync(stoppingToken))
                {
                    try
                    {
                        await RunOnceAsync(stoppingToken);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                    {
                        // apagando
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error ejecutando ciclo de expiración de órdenes");
                    }
                }
            }
            finally
            {
                _logger.LogInformation("ExpireAwaitingPayment job detenido.");
            }
        }

        private async Task RunOnceAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var now = DateTime.UtcNow;

            // 1) Selecciona candidatos en lote (solo IDs para reducir payload)
            var candidateIds = await db.Set<Order>()
                .AsNoTracking()
                .Where(o => !o.IsDeleted && o.Active
                            && o.Status == OrderStatus.AcceptedAwaitingPayment
                            && o.PaymentImageUrl == null
                            && o.AutoCloseAt != null
                            && o.AutoCloseAt <= now)
                .OrderBy(o => o.AutoCloseAt)
                .Select(o => o.Id)
                .Take(_opts.BatchSize)
                .ToListAsync(ct);

            if (candidateIds.Count == 0) return;

            int expiredCount = 0;

            // 2) Procesa cada orden con relectura y guardas (idempotente)
            foreach (var orderId in candidateIds)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    using var scopeItem = _scopeFactory.CreateScope();
                    var dbItem = scopeItem.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var emailItem = scopeItem.ServiceProvider.GetRequiredService<IOrderEmailService>();
                    var userRepo = scopeItem.ServiceProvider.GetRequiredService<IUserRepository>();
                    var producerRepo = scopeItem.ServiceProvider.GetRequiredService<IProducerRepository>();
                    var notifSvc = scopeItem.ServiceProvider.GetRequiredService<INotificationService>(); 

                    // Releer con tracking
                    var order = await dbItem.Set<Order>().FirstOrDefaultAsync(o => o.Id == orderId, ct);
                    if (order is null) continue;
                    if (order.IsDeleted || !order.Active) continue;

                    // Guardas anti-race
                    if (order.Status != OrderStatus.AcceptedAwaitingPayment) continue;
                    if (!string.IsNullOrWhiteSpace(order.PaymentImageUrl)) continue;
                    if (order.AutoCloseAt == null || order.AutoCloseAt > DateTime.UtcNow) continue;

                    // 3) Transición a Expired
                    order.Status = OrderStatus.Expired;
                    // Dejamos AutoCloseAt como audit trail

                    await dbItem.SaveChangesAsync(ct);
                    expiredCount++;

                    // 4) Emails (opcional)
                    if (_opts.SendEmails)
                    {
                        try
                        {
                            var customer = await userRepo.GetContactUser(order.UserId);
                            if (customer != null)
                            {
                                await emailItem.SendOrderExpiredByNoPaymentToCustomer(
                                    emailReceptor: customer.Email,
                                    orderId: order.Id,
                                    productName: order.ProductNameSnapshot,
                                    quantityRequested: order.QuantityRequested,
                                    total: order.Total,
                                    expiredAtUtc: DateTime.UtcNow
                                );
                            }

                            var producer = await producerRepo.GetContactProducer(order.ProducerIdSnapshot);
                            if (producer != null)
                            {
                                // Si tienes plantilla para productor, úsala; si no, omite.
                                // Ejemplo opcional:
                                // await emailItem.SendOrderExpiredToProducer(...);
                            }
                        }
                        catch (Exception exEmail)
                        {
                            _logger.LogError(exEmail, "Error enviando correo de expiración para OrderId {OrderId}", order.Id);
                        }
                    }

                    // 5) Notificaciones (SIEMPRE, best-effort)
                    try
                    {
                        // Cliente
                        await notifSvc.CreateAsync(new CreateNotificationRequest
                        {
                            UserId = order.UserId,
                            Title = "Pedido expirado",
                            Message = $"Tu pedido {order.Id} expiró por no cargar el comprobante de pago a tiempo.",
                            RelatedType = "Order",
                            RelatedRoute = $"/account/orders/{order.Code}"
                        }, ct);

                        // Productor (útil para liberar seguimiento)
                        var producerNtc = await producerRepo.GetByIdAsync(order.ProducerIdSnapshot)
                            ?? throw new BusinessException("No se pudo obtener el contacto del productor.");
                        await notifSvc.CreateAsync(new CreateNotificationRequest
                        {
                            UserId = producerNtc.UserId,
                            Title = "Pedido expirado del cliente",
                            Message = $"El pedido {order.Id} expiró por falta de comprobante de pago del cliente.",
                            RelatedType = "Order",
                            RelatedRoute = $"/account/producer/orders/{order.Code}"
                        }, ct);
                    }
                    catch (Exception exNotif)
                    {
                        _logger.LogError(exNotif, "Error enviando notificaciones (expire) OrderId {OrderId}", order.Id);
                    }
                }
                catch (DbUpdateConcurrencyException)
                {
                    // Otro proceso/instancia la tocó; ignorar
                }
                catch (Exception exItem)
                {
                    _logger.LogError(exItem, "Error expirando OrderId {OrderId}", orderId);
                }
            }

            _logger.LogInformation("Job expiró {Count} órdenes (lote {Batch})", expiredCount, candidateIds.Count);
        }
    }
}
