using MaisonCalliard.Application.Menu;
using MaisonCalliard.Application.Menu.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MaisonCalliard.Api.Controllers;

[ApiController]
[Route("api/menu")]
public sealed class MenuController : ControllerBase
{
    private readonly IMenuService _menuService;

    public MenuController(IMenuService menuService)
    {
        _menuService = menuService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CancellationToken cancellationToken)
    {
        var items = await _menuService.GetAllAsync(cancellationToken);
        return Ok(items);
    }

    [Authorize(Roles = "admin")]
    [HttpPost]
    public async Task<IActionResult> Create([FromForm] CreateMenuItemRequest request, IFormFile? image, CancellationToken cancellationToken)
    {
        Stream? stream = null;
        string? fileName = null;
        string? contentType = null;

        if (image is not null)
        {
            stream = image.OpenReadStream();
            fileName = image.FileName;
            contentType = image.ContentType;
        }

        var result = await _menuService.CreateAsync(request, stream, fileName, contentType, cancellationToken);
        return CreatedAtAction(nameof(GetAll), new { id = result.Id }, result);
    }

    [Authorize(Roles = "admin")]
    [HttpPut("reorder")]
    public async Task<IActionResult> Reorder([FromBody] ReorderMenuItemsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            await _menuService.ReorderAsync(request, cancellationToken);
            return NoContent();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { title = ex.Message });
        }
    }

    [Authorize(Roles = "admin")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromForm] UpdateMenuItemRequest request, IFormFile? image, CancellationToken cancellationToken)
    {
        Stream? stream = null;
        string? fileName = null;
        string? contentType = null;

        if (image is not null)
        {
            stream = image.OpenReadStream();
            fileName = image.FileName;
            contentType = image.ContentType;
        }

        try
        {
            var result = await _menuService.UpdateAsync(id, request, stream, fileName, contentType, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [Authorize(Roles = "admin")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _menuService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
