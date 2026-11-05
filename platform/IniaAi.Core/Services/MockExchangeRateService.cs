using IniaAi.Core.Services.Abstraction;

namespace IniaAi.Core.Services;

public class MockExchangeRateService : IExchangeRateService
{
    // Dummy implementation with hardcoded rates
    // In a real implementation, this would call an external API
    public Task<decimal> GetExchangeRateAsync(string fromCurrency, string toCurrency, DateTime date)
    {
        // Hardcoded rates for demonstration
        var rates = new Dictionary<(string from, string to), decimal>
        {
            { ("USD", "EUR"), 0.92m },
            { ("EUR", "USD"), 1.09m },
            { ("USD", "TRY"), 32.5m },
            { ("TRY", "USD"), 0.031m },
            { ("EUR", "TRY"), 35.3m },
            { ("TRY", "EUR"), 0.028m }
        };

        // If currencies are the same, return 1
        if (fromCurrency == toCurrency)
        {
            return Task.FromResult(1m);
        }

        // Try to get the direct rate
        if (rates.TryGetValue((fromCurrency, toCurrency), out decimal rate))
        {
            return Task.FromResult(rate);
        }

        // If no direct rate found, throw an exception
        // In a real implementation, you might want to handle this differently
        throw new NotSupportedException($"Currency conversion from {fromCurrency} to {toCurrency} is not supported in the dummy implementation.");
    }
}
