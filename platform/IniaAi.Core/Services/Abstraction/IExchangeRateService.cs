namespace IniaAi.Core.Services.Abstraction;

public interface IExchangeRateService
{
    Task<decimal> GetExchangeRateAsync(string fromCurrency, string toCurrency, DateTime date);
}
