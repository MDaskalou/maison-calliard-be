using MaisonCalliard.Application.Products;
using MaisonCalliard.Application.Products.Dtos;
using MaisonCalliard.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MaisonCalliard.Api.Controllers;

[ApiController]
[Route("api/products")]
public sealed class ProductsController : ControllerBase
{
    private readonly IProductService _productService;

    public ProductsController(IProductService productService)
    {
        _productService = productService;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll(CakeStyle? style, CancellationToken cancellationToken)
    {
        var products = await _productService.GetAllAsync(style, cancellationToken);
        return Ok(products);
    }

    [Authorize(Roles = "admin")]
    [HttpPost]
    public async Task<IActionResult> Create([FromForm] CreateProductRequest request, IFormFile? image, CancellationToken cancellationToken)
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
            var result = await _productService.CreateAsync(request, stream, fileName, contentType, cancellationToken);
            return CreatedAtAction(nameof(GetAll), new { id = result.Id }, result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { title = ex.Message });
        }
    }

    [Authorize(Roles = "admin")]
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromForm] UpdateProductRequest request, IFormFile? image, CancellationToken cancellationToken)
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
            var result = await _productService.UpdateAsync(id, request, stream, fileName, contentType, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { title = ex.Message });
        }
    }

    [Authorize(Roles = "admin")]
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            await _productService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [Authorize(Roles = "admin")]
    [HttpPatch("{id:guid}/availability")]
    public async Task<IActionResult> ToggleAvailability(Guid id, CancellationToken cancellationToken)
    {
        try
        {
            var result = await _productService.ToggleAvailabilityAsync(id, cancellationToken);
            return Ok(result);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }
}
