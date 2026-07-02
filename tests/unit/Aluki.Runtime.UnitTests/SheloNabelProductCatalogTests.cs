using Aluki.Runtime.SheloNabel;
using Xunit;

namespace Aluki.Runtime.UnitTests;

/// <summary>
/// The catalog was compacted to one dense line per product to cut per-message
/// prompt tokens (it is re-sent on every message). These tests pin that the
/// compaction never drops a product, a price, or a safety fact.
/// </summary>
[Trait("Category", "Unit")]
public sealed class SheloNabelProductCatalogTests
{
    private static readonly string SystemPrompt = new SheloNabelPromptBuilder().BuildSystemPrompt(null);

    [Theory]
    // Línea Baba de Caracol
    [InlineData("Crema Facial Baba de Caracol")]
    [InlineData("Crema Corporal Baba de Caracol")]
    [InlineData("Suero Baba de Caracol")]
    [InlineData("Shampoo Baba de Caracol")]
    [InlineData("Jabón Baba de Caracol")]
    [InlineData("Kit Familia Baba de Caracol")]
    // Capilar
    [InlineData("Cabello Sano Shampoo")]
    [InlineData("Papa Acondicionador")]
    [InlineData("Crema Nutritiva para Peinar")]
    [InlineData("Hidratante Capilar Ampolletas")]
    [InlineData("Shampoo de Cebolla")]
    [InlineData("Retocador de Canas")]
    // Facial / cosméticos
    [InlineData("Botox Noche")]
    [InlineData("Suero de Centella Asiática")]
    [InlineData("Mascarilla con Carbón Activado")]
    [InlineData("Agua Termal")]
    [InlineData("Maquillaje BB")]
    [InlineData("Parches Protectores para Granitos")]
    [InlineData("Parches Colágeno Contorno de Ojos")]
    // Corporal
    [InlineData("Crema Reafirmante Corporal")]
    [InlineData("Pomada Árnica y Ocote")]
    [InlineData("Chile Gel")]
    [InlineData("Crema Corporal D-B-T")]
    [InlineData("Crema de Manos Uva")]
    // Bienestar
    [InlineData("Té RDX")]
    [InlineData("Gomitas con Probióticos")]
    [InlineData("Colágeno Hidrolizado")]
    [InlineData("Para Ellas +35")]
    [InlineData("Para Ellos +35")]
    [InlineData("Magnesio + Maca")]
    [InlineData("Shelo Transfer")]
    [InlineData("NAD Colágeno + Resveratrol")]
    [InlineData("Gomitas Vinagre de Manzana")]
    [InlineData("Vita Niños")]
    // Esencias / higiene
    [InlineData("Serenidad")]
    [InlineData("Pasta Dental")]
    [InlineData("Gel Antibacterial")]
    [InlineData("Repelente Mosquitos")]
    [InlineData("Desodorante Para Pies")]
    public void Catalog_keeps_every_product(string productName)
    {
        Assert.Contains(productName, SystemPrompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("$289")]  // Crema Facial
    [InlineData("$377")]  // Suero
    [InlineData("$1,605")] // Kit Familia
    [InlineData("$229")]  // Pomada Árnica
    [InlineData("$500 MXN")] // pedido mínimo
    public void Catalog_keeps_key_prices(string price)
    {
        Assert.Contains(price, SystemPrompt, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("NO de día")]                 // Botox Noche solo nocturno
    [InlineData("NO cerca de ojos")]          // Chile Gel
    [InlineData("consultar médico en embarazo")] // Suero
    [InlineData("no en heridas abiertas")]    // Pomada Árnica
    public void Catalog_keeps_safety_facts(string safetyFact)
    {
        Assert.Contains(safetyFact, SystemPrompt, StringComparison.Ordinal);
    }
}
