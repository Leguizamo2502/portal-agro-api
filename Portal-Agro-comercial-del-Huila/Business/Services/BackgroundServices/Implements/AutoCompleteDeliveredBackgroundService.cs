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
    public sealed class AutoCompleteDeliveredBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AutoCompleteDeliveredBackgroundService> _logger;
        private readonly AutoCompleteDeliveredJobOptions _opts;

        public AutoCompleteDeliveredBackgroundService(
            IServiceScopeFactory scopeFactory,
            IOptions<AutoCompleteDeliveredJobOptions> opts,
            ILogger<AutoCompleteDeliveredBackgroundService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _opts = opts.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var interval = TimeSpan.FromSeconds(Math.Max(30, _opts.ScanIntervalSeconds));
            var timer = new PeriodicTimer(interval);
            _logger.LogInformation("AutoCompleteDelivered job iniciado.");

            while (!stoppingToken.IsCancellationRequested &&
                   await timer.WaitForNextTickAsync(stoppingToken))
            {
                try { await RunOnceAsync(stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
                catch (Exception ex) { _logger.LogError(ex, "Error en ciclo de autocierre"); }
            }
        }

        private async Task RunOnceAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var now = DateTime.UtcNow;

            var ids = await db.Set<Order>()
                .AsNoTracking()
                .Where(o => !o.IsDeleted && o.Active
                            && o.Status == OrderStatus.DeliveredPendingBuyerConfirm
                            && o.AutoCloseAt != null && o.AutoCloseAt <= now)
                .OrderBy(o => o.AutoCloseAt)
                .Select(o => o.Id)
                .Take(_opts.BatchSize)
                .ToListAsync(ct);

            if (ids.Count == 0) return;

            var done = 0;

            foreach (var id in ids)
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    using var itemScope = _scopeFactory.CreateScope();
                    var dbItem = itemScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var emailItem = itemScope.ServiceProvider.GetRequiredService<IOrderEmailService>();
                    var userRepoItem = itemScope.ServiceProvider.GetRequiredService<IUserRepository>();
                    var producerRepoItem = itemScope.ServiceProvider.GetRequiredService<IProducerRepository>();
                    var notifSvc = itemScope.ServiceProvider.GetRequiredService<INotificationService>(); // <- en el itemScope

                    var order = await dbItem.Set<Order>().FirstOrDefaultAsync(o => o.Id == id, ct);
                    if (order == null || order.IsDeleted || !order.Active) continue;
                    if (order.Status != OrderStatus.DeliveredPendingBuyerConfirm) continue;
                    if (order.AutoCloseAt == null || order.AutoCloseAt > DateTime.UtcNow) continue;

                    // 1) Autocierre: completar
                    order.Status = OrderStatus.Completed;
                    order.UserReceivedAnswer = UserReceivedAnswer.Yes; // autoconsentido
                    order.UserReceivedAt = DateTime.UtcNow;
                    order.AutoCloseAt = null;

                    await dbItem.SaveChangesAsync(ct);
                    done++;

                    // 2) Emails (opcional, según flag)
                    if (_opts.SendEmails)
                    {
                        try
                        {
                            var producer = await producerRepoItem.GetContactProducer(order.ProducerIdSnapshot);
                            if (producer != null)
                            {
                                await emailItem.SendOrderCompletedToProducer(
                                    emailReceptor: producer.Email,
                                    orderId: order.Id,
                                    productName: order.ProductNameSnapshot,
                                    quantityRequested: order.QuantityRequested,
                                    total: order.Total,
                                    completedAtUtc: order.UserReceivedAt!.Value
                                );
                            }

                            var customer = await userRepoItem.GetContactUser(order.UserId);
                            if (customer != null)
                            {
                                await emailItem.SendOrderCompletedToCustomer(
                                    emailReceptor: customer.Email,
                                    orderId: order.Id,
                                    productName: order.ProductNameSnapshot,
                                    quantityRequested: order.QuantityRequested,
                                    total: order.Total,
                                    completedAtUtc: order.UserReceivedAt!.Value,
                                    autoCompleted: true
                                );
                            }
                        }
                        catch (Exception exMail)
                        {
                            _logger.LogError(exMail, "Error enviando correos de autocierre OrderId {OrderId}", order.Id);
                        }
                    }

                    // 3) Notificaciones (best-effort)
                    try
                    {
                        var producerNtc = await producerRepoItem.GetByIdAsync(order.ProducerIdSnapshot)
                            ?? throw new BusinessException("No se pudo obtener el contacto del productor.");
                        // Productor
                        await notifSvc.CreateAsync(new CreateNotificationRequest
                        {
                            UserId = producerNtc.UserId,
                            Title = "Pedido completado automáticamente",
                            Message = $"El pedido {order.Id} se completó por tiempo expirado sin confirmación del cliente.",
                            RelatedType = "Order",
                            RelatedRoute = $"/account/producer/orders/{order.Code}"
                        }, ct);

                        // Cliente
                        await notifSvc.CreateAsync(new CreateNotificationRequest
                        {
                            UserId = order.UserId,
                            Title = "Pedido completado automáticamente",
                            Message = $"Tu pedido {order.Id} se marcó como completado automáticamente.",
                            RelatedType = "Order",
                            RelatedRoute = $"/account/orders/{order.Code}"
                        }, ct);
                    }
                    catch (Exception exNotif)
                    {
                        _logger.LogError(exNotif, "Error enviando notificaciones (auto-complete) OrderId {OrderId}", order.Id);
                    }
                }
                catch (DbUpdateConcurrencyException ex)
                {
                    _logger.LogWarning(ex, "Concurrency al autocerrar OrderId {OrderId}. Otro proceso la modificó; se omite.", id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error autocerrando OrderId {OrderId}", id);
                }
            }

            _logger.LogInformation("AutoCompleteDelivered: completadas {Count} / lote {Batch}", done, ids.Count);
        }
    }
}
