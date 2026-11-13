using Xunit;
using Refugio360.Api;

namespace Refugio360.Api.Tests;

public sealed class RequestValidatorTests
{
    [Fact]
    public void Valid_occurrence_is_accepted()
    {
        var request = new CreateOccurrenceRequest("Enchente", "Alagamento em uma rua", -23.55, -46.63);
        Assert.Empty(RequestValidator.ValidateOccurrence(request));
    }

    [Fact]
    public void Occurrence_with_invalid_coordinates_is_rejected()
    {
        var request = new CreateOccurrenceRequest("Enchente", "Alagamento em uma rua", 120, -46.63);
        Assert.Contains("coordenadas", RequestValidator.ValidateOccurrence(request).Keys);
    }

    [Fact]
    public void Occurrence_without_category_is_rejected()
    {
        var request = new CreateOccurrenceRequest(" ", "Alagamento em uma rua", -23.55, -46.63);
        Assert.Contains("categoria", RequestValidator.ValidateOccurrence(request).Keys);
    }
}
