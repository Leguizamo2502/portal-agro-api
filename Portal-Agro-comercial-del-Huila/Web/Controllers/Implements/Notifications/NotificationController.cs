using Business.Interfaces.Implements.Notification;
using Entity.Domain.Models.Implements.Auth;
using Entity.DTOs.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using Utilities.Helpers.Auth;

namespace Web.Controllers.Implements.Notifications
{
    [Authorize]
    [ApiController]
    [Route("api/v1/[controller]")]
    public class NotificationController : ControllerBase
    {
        private readonly INotificationService _service;
        private readonly ILogger<NotificationController> _logger;

        public NotificationController(INotificationService service, ILogger<NotificationController> logger)
        {
            _service = service;
            _logger = logger;
        }

        // GET api/notification/unread?take=20
        [HttpGet("unread")]
        public async Task<ActionResult<IReadOnlyList<NotificationListItemDto>>> GetUnread([FromQuery] int take = 20, CancellationToken ct = default)
        {
            var userId = GetUserIdOrThrow();
            if (take <= 0) take = 20;

            var list = await _service.GetUnreadAsync(userId, take, ct);
            return Ok(list);
        }

        // GET api/notification/count
        [HttpGet("count")]
        public async Task<ActionResult<int>> CountUnread(CancellationToken ct = default)
        {
            var userId = GetUserIdOrThrow();
            var count = await _service.CountUnreadAsync(userId, ct);
            return Ok(count);
        }

        // GET api/notification/history?page=1&pageSize=20
        [HttpGet("history")]
        public async Task<ActionResult<(IReadOnlyList<NotificationListItemDto> Items, int Total)>> History(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            CancellationToken ct = default)
        {
            var userId = GetUserIdOrThrow();
            if (page <= 0) page = 1;
            if (pageSize <= 0) pageSize = 20;

            var result = await _service.GetHistoryAsync(userId, page, pageSize, ct);
            return Ok(result);
        }

        // PUT api/notification/{id}/read
        [HttpPut("{id:int}/read")]
        public async Task<IActionResult> MarkAsRead([FromRoute] int id, CancellationToken ct = default)
        {
            var userId = GetUserIdOrThrow();
            var ok = await _service.MarkAsReadAsync(id, userId, ct);
            if (!ok) return NotFound();
            return NoContent();
        }

        // OPCIONAL (solo para pruebas manuales con Postman)
        // POST api/notification
        [HttpPost]
        public async Task<ActionResult<int>> Create([FromBody] CreateNotificationRequest request, CancellationToken ct = default)
        {
            // Si no envías UserId en el body, se asume el del token
            if (request.UserId <= 0)
                request.UserId = GetUserIdOrThrow();

            var id = await _service.CreateAsync(request, ct);
            return CreatedAtAction(nameof(GetUnread), new { id }, id);
        }

        private int GetUserIdOrThrow()
        {
            var userId = HttpContext.GetUserId();

            return userId;
        }
    }
}
