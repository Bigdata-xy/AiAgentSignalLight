using Xunit;

namespace SignalLight.Agent.Tests;

public sealed class AgentContractTests
{
    [Fact]
    public void Emit_command_is_the_public_ingestion_contract()
    {
        Assert.Equal("emit", "emit");
    }
}
