using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json.Serialization;
using System.Text.Json;
using Xunit;

namespace Tests
{
    public enum FormState
    {
        Viewed,
        Sent,
        Funded,
        Disqualified,
        Withdrawn,
    }

    public abstract partial record FormHistory([JsonDiscriminator] FormState State, DateTimeOffset DateCreated);

    [JsonDiscriminatorFallback]
    public record FormHistoryStatusChange(FormState State) : FormHistory(State, DateTimeOffset.UtcNow);

    public record FormHistoryFunded() : FormHistory(FormState.Funded, DateTimeOffset.UtcNow)
    {
        public int Amount { get; set; }
    }

    public record FormHistoryDisqualified() : FormHistory(FormState.Disqualified, DateTimeOffset.UtcNow)
    {
        public string? Reason { get; set; }
    }

    public class FallbackTests
    {
        [Fact]
        public void TestFallbackSerialization()
        {
            var items = new FormHistory[]
            {
                new FormHistoryStatusChange(FormState.Viewed),
                new FormHistoryStatusChange(FormState.Sent),
                new FormHistoryFunded { Amount = 10_000 },
            };

            var serialized = JsonSerializer.Serialize(items);

            // Deserialized
            var deserialized = JsonSerializer.Deserialize<FormHistory[]>(serialized);

            if (deserialized is null)
                throw new System.Exception("Unable to deserialize");

            Assert.Equal(items.Length, deserialized.Length);

            for (var i = 0; i < items.Length; ++i)
                Assert.Equal(items[i], deserialized[i]);
        }
    }
}
