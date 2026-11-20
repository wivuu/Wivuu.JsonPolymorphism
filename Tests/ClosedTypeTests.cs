#if NET11_0_OR_GREATER
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Tests
{
    public enum PaymentMethod
    {
        Card,
        BankTransfer,
        Wallet,
    }

    public enum WalletProvider
    {
        PayPal,
        Venmo,
    }

    // 'closed' on a different partial declaration than the discriminator
    public closed partial record Payment;

    public partial record Payment([JsonDiscriminator] PaymentMethod Method, decimal Amount);

    public record CardPayment(decimal Amount, string Last4) : Payment(PaymentMethod.Card, Amount);

    public record BankTransferPayment(decimal Amount, string Iban) : Payment(PaymentMethod.BankTransfer, Amount);

    // Nested closed type with its own discriminator
    public closed partial record WalletPayment([JsonDiscriminator] WalletProvider Provider, decimal Amount)
        : Payment(PaymentMethod.Wallet, Amount);

    public record PayPalPayment(decimal Amount, string Email) : WalletPayment(WalletProvider.PayPal, Amount);

    public record VenmoPayment(decimal Amount, string Handle) : WalletPayment(WalletProvider.Venmo, Amount);

    public enum SensorKind
    {
        Temperature,
        Humidity,
        Pressure,
        Light,
    }

    public closed partial class SensorReading
    {
        [JsonDiscriminator]
        public virtual SensorKind Kind { get; init; }

        public double Value { get; init; }
    }

    public sealed class TemperatureReading : SensorReading
    {
        public override SensorKind Kind => SensorKind.Temperature;
    }

    public sealed class HumidityReading : SensorReading
    {
        public override SensorKind Kind => SensorKind.Humidity;
    }

    [JsonDiscriminatorFallback]
    public sealed class OtherReading : SensorReading
    {
        public override SensorKind Kind { get; init; }
    }

    public class ClosedTypeTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void TestClosedRecords(bool stringEnums)
        {
            var options = new JsonSerializerOptions();

            if (stringEnums)
                options.Converters.Add(new JsonStringEnumConverter());

            Payment[] payments =
            {
                new CardPayment(10m, "4242"),
                new BankTransferPayment(20m, "DE89370400440532013000"),
                new PayPalPayment(30m, "someone@example.com"),
                new VenmoPayment(40m, "@someone"),
            };

            var serialized   = JsonSerializer.Serialize(payments, options);
            var deserialized = JsonSerializer.Deserialize<Payment[]>(serialized, options);

            Assert.Equal(payments, deserialized);

            // A concrete subtype serialized directly still includes its discriminator
            Assert.Equal(
                payments[2],
                JsonSerializer.Deserialize<Payment>(JsonSerializer.Serialize((PayPalPayment)payments[2], options), options));
        }

        [Fact]
        public void TestClosedClassWithFallback()
        {
            SensorReading[] readings =
            {
                new TemperatureReading { Value = 21.5 },
                new HumidityReading { Value = 0.4 },
                new OtherReading { Kind = SensorKind.Pressure, Value = 1013 },
            };

            var serialized   = JsonSerializer.Serialize(readings);
            var deserialized = JsonSerializer.Deserialize<SensorReading[]>(serialized);

            Assert.NotNull(deserialized);
            Assert.Equal(readings.Length, deserialized.Length);

            for (var i = 0; i < readings.Length; ++i)
            {
                Assert.IsType(readings[i].GetType(), deserialized[i]);
                Assert.Equal(readings[i].Kind, deserialized[i].Kind);
                Assert.Equal(readings[i].Value, deserialized[i].Value);
            }

            Assert.Equal(typeof(OtherReading), SensorReading.GetType(SensorKind.Light));
        }
    }
}
#endif
