using FluentAssertions;
using InertiaNet.Core;
using InertiaNet.Extensions;
using InertiaNet.Middleware;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace InertiaNet.Tests.Middleware;

public class ValidationErrorKeysTests
{
    [Theory]
    [InlineData("Email", "email", "email")]
    [InlineData("email", "email", "email")]
    [InlineData("FirstName", "firstName", "firstName")]
    [InlineData("Address.Street", "address.street", "address.street")]
    [InlineData("address.street", "address.street", "address.street")]
    [InlineData("Customer.Address.PostCode", "customer.address.postCode", "customer.address.postCode")]
    [InlineData("Values[3].Code", "values[3].code", "values.3.code")]
    [InlineData("values.3.code", "values.3.code", "values.3.code")]
    [InlineData("Values[3]", "values[3]", "values.3")]
    [InlineData("Grid[1][2].Cell", "grid[1][2].cell", "grid.1.2.cell")]
    [InlineData("Orders[0].Lines[12].Sku", "orders[0].lines[12].sku", "orders.0.lines.12.sku")]
    [InlineData("[0].Name", "[0].name", "0.name")]
    [InlineData("Prices[EUR].Amount", "prices[EUR].amount", "prices.EUR.amount")]
    [InlineData("Meta[a.b].Value", "meta[a.b].value", "meta.a.b.value")]
    [InlineData("", "", "")]
    [InlineData("$", "", "")]
    [InlineData("$.Address.Street", "address.street", "address.street")]
    [InlineData("$.items[0].name", "items[0].name", "items.0.name")]
    [InlineData("$['odd.name']", "['odd.name']", "odd.name")]
    [InlineData("Broken[0", "broken[0", "broken[0")]
    public void Normalize_AppliesTheNamingPolicyPerSegment(string key, string brackets, string dots)
    {
        Keys(ValidationErrorKeyFormat.Brackets).Normalize(key).Should().Be(brackets);
        Keys(ValidationErrorKeyFormat.Dots).Normalize(key).Should().Be(dots);
    }

    [Theory]
    [InlineData("Address.Street")]
    [InlineData("Values[3].Code")]
    [InlineData("API_KEY")]
    public void Preserve_ForwardsKeysUnchanged(string key)
        => Keys(ValidationErrorKeyFormat.Preserve).Normalize(key).Should().Be(key);

    [Fact]
    public void ConfiguredSerializerPolicies_ReplaceCamelCase()
    {
        var keys = ValidationErrorKeys.From(new InertiaOptions
        {
            JsonSerializerOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
                DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            },
            ValidationErrorKeyFormat = ValidationErrorKeyFormat.Dots,
        });

        keys.Normalize("ShippingAddress.PostCode").Should().Be("shipping_address.post_code");
        keys.Normalize("LineItems[2].UnitPrice").Should().Be("line_items.2.unit_price");
        keys.Normalize("Prices[EurRate].Amount").Should().Be("prices.eurRate.amount");
    }

    [Fact]
    public void DefaultOptions_UseCamelCaseAndBrackets()
    {
        var keys = ValidationErrorKeys.From((InertiaOptions?)null);

        keys.Format.Should().Be(ValidationErrorKeyFormat.Brackets);
        keys.Normalize("Values[3].Code").Should().Be("values[3].code");
        ValidationErrorKeys.From(new InertiaOptions { JsonSerializerOptions = new JsonSerializerOptions() })
            .Normalize("Address.Street").Should().Be("address.street");
    }

    [Fact]
    public void Normalize_MergesMessagesOfKeysThatCollide()
    {
        var errors = Keys(ValidationErrorKeyFormat.Dots).Normalize(new Dictionary<string, string[]>
        {
            ["Items[0].Name"] = ["Required"],
            ["items.0.name"] = ["Too short"],
            ["Email"] = ["Invalid"],
        });

        errors.Should().HaveCount(2);
        errors["items.0.name"].Should().Equal("Required", "Too short");
        errors["email"].Should().Equal("Invalid");
    }

    [Fact]
    public void UndefinedFormat_FailsOptionsValidation()
    {
        var services = new ServiceCollection();
        services.AddInertia(options => options.ValidationErrorKeyFormat = (ValidationErrorKeyFormat)99);
        using var provider = services.BuildServiceProvider();

        var resolve = () => provider.GetRequiredService<IOptions<InertiaOptions>>().Value;

        resolve.Should().Throw<OptionsValidationException>();
    }

    private static ValidationErrorKeys Keys(ValidationErrorKeyFormat format)
        => ValidationErrorKeys.From(new InertiaOptions { ValidationErrorKeyFormat = format });
}
