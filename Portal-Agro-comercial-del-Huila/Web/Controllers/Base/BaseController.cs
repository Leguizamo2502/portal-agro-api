﻿using Entity.DTOs.BaseDTO;
using Microsoft.AspNetCore.Mvc;
using Utilities.Exceptions;

namespace Web.Controllers.Base
{
    [ApiController]
    [Route("api/v1/[controller]")]
    public abstract class BaseController<TDto, TSelect, TService> : ControllerBase
        where TDto : class
        where TService : class
    {
        protected readonly TService _service;
        protected readonly ILogger _logger;

        protected BaseController(TService service, ILogger logger)
        {
            _service = service;
            _logger = logger;
        }

        [HttpGet]
        //[ProducesResponseType(typeof(IEnumerable<TDto>), 200)]
        [ProducesResponseType(200)]
        [ProducesResponseType(500)]
        public virtual async Task<IActionResult> Get()
        {
            try
            {
                var result = await GetAllAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error obteniendo datos");
                return StatusCode(500, new { message = "Error interno del servidor." });
            }

          
        }

        [HttpGet("{id}")]
        //[ProducesResponseType(typeof(TDto), 200)]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public virtual async Task<IActionResult> GetById(int id)
        {
            try
            {
                var result = await GetByIdAsync(id);
                if (result == null)
                    return NotFound(new { message = $"No se encontró el elemento con ID {id}" });

                return Ok(result);
            }
            catch (ValidationException ex)
            {
                _logger.LogWarning(ex, "Validación fallida con ID: {Id}", id);
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al obtener el ID {Id}", id);
                return StatusCode(500, new { message = "Error interno del servidor." });
            }
        }

        [HttpPost]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(500)]
        public virtual async Task<IActionResult> Post([FromBody] TDto dto)
        {
            try
            {
                await AddAsync(dto);
                return Ok(new { message = "Elemento agregado exitosamente" });
            }
            catch (ValidationException ex)
            {
                _logger.LogWarning(ex, "Validación fallida al agregar");
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al agregar elemento");
                return StatusCode(500, new { message = "Error interno del servidor." });
            }
        }


        [HttpPut("{id}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public virtual async Task<IActionResult> Put(int id, [FromBody] TDto dto)
        {
            try
            {
                if (dto is not BaseDto identifiableDto)
                    return BadRequest(new { message = "El DTO no implementa IHasId." });

                identifiableDto.Id = id;
                var updated = await UpdateAsync(id, dto);
                if (!updated)
                    return NotFound(new { message = $"No se encontró el recurso con ID {id} para actualizar." });

                return Ok(new { message = "Elemento actualizado exitosamente." });
            }
            catch (ValidationException ex)
            {
                _logger.LogWarning(ex, "Validación fallida al actualizar ID: {Id}", id);
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al actualizar ID: {Id}", id);
                return StatusCode(500, new { message = "Error interno del servidor." });
            }
        }




        [HttpDelete("{id}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                var result = await DeleteAsync(id);

                if (!result)
                    return NotFound(new { message = "No se pudo eliminar el recurso." });

                return Ok(new { message = $"Eliminación realizada correctamente." });
            }
            catch (ValidationException ex)
            {
                _logger.LogWarning(ex, "Validación fallida al eliminar recurso con id: {ResourceId}", id);
                return BadRequest(new { message = ex.Message });
            }
            catch (EntityNotFoundException ex)
            {
                _logger.LogInformation(ex, "Recurso no encontrado para eliminar con id: {ResourceId}", id);
                return NotFound(new { message = ex.Message });
            }
            catch (ExternalServiceException ex)
            {
                _logger.LogError(ex, "Error en servicio externo al eliminar recurso con id: {ResourceId}", id);
                return StatusCode(500, new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado al eliminar recurso con id: {ResourceId}", id);
                return StatusCode(500, new { message = "Error interno del servidor." });
            }
        }

        [HttpPatch("{id}")]
        [ProducesResponseType(200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> DeleteLogic(int id)
        {
            try
            {
                var result = await DeleteLogicAsync(id);

                if (!result)
                    return NotFound(new { message = "No se pudo eliminar el recurso." });

                return Ok(new { message = $"Eliminación logica realizada correctamente." });
            }
            catch (ValidationException ex)
            {
                _logger.LogWarning(ex, "Validación fallida al eliminar recurso con id: {ResourceId}", id);
                return BadRequest(new { message = ex.Message });
            }
            catch (EntityNotFoundException ex)
            {
                _logger.LogInformation(ex, "Recurso no encontrado para eliminar con id: {ResourceId}", id);
                return NotFound(new { message = ex.Message });
            }
            catch (ExternalServiceException ex)
            {
                _logger.LogError(ex, "Error en servicio externo al eliminar recurso con id: {ResourceId}", id);
                return StatusCode(500, new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado al eliminar recurso con id: {ResourceId}", id);
                return StatusCode(500, new { message = "Error interno del servidor." });
            }
        }


        // Métodos que las subclases deben implementar
        protected abstract Task<IEnumerable<TSelect>> GetAllAsync();
        protected abstract Task<TSelect?> GetByIdAsync(int id);
        protected abstract Task AddAsync(TDto dto);
        protected abstract Task<bool> UpdateAsync(int id, TDto dto);
        protected abstract Task<bool> DeleteAsync(int id);
        protected abstract Task<bool> DeleteLogicAsync(int id);

    }


}

