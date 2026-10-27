using StoreFlow.Domain.Entities;
using StoreFlow.Domain.Money;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace StoreFlow.Infrastructure.Persistence;

/// <summary>Value converters shared by the entity configurations.</summary>
internal static class Converters
{
    /// <summary>
    /// Stores money as an integer count of minor units. The conversion is lossless and keeps every
    /// monetary sum exact, which floating point storage could never guarantee.
    /// </summary>
    public static readonly ValueConverter<Money, long> MoneyToMinorUnits = new(
        money => money.ToMinorUnits(),
        value => Money.FromMinorUnits(value));
}
