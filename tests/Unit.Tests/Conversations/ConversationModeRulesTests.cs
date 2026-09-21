using WhatsAppMonitorAssistant.Modules.Conversations.Contracts;
using WhatsAppMonitorAssistant.Modules.Conversations.Domain;

namespace WhatsAppMonitorAssistant.Unit.Tests.Conversations;

/// <summary>
/// The AI/Human/Closed state machine: only an explicit request leaves Human, and a closed
/// conversation is never reopened.
/// </summary>
public sealed class ConversationModeRulesTests
{
    [Fact]
    public void The_documented_stored_mode_values_are_exposed_as_contract_modes()
    {
        Assert.Equal("AI", ConversationModes.Ai);
        Assert.Equal("Human", ConversationModes.Human);
        Assert.Equal("Closed", ConversationModes.Closed);

        Assert.Equal(ConversationMode.Ai, ConversationModes.ToContract(ConversationModes.Ai));
        Assert.Equal(ConversationMode.Human, ConversationModes.ToContract(ConversationModes.Human));
        Assert.Equal(ConversationMode.Closed, ConversationModes.ToContract(ConversationModes.Closed));
    }

    [Theory]
    [InlineData("AI")]
    [InlineData("Human")]
    [InlineData("Closed")]
    public void A_canonical_stored_mode_round_trips_through_the_contract_mode(string mode) =>
        Assert.Equal(mode, ConversationModes.FromContract(ConversationModes.ToContract(mode)));

    [Fact]
    public void Only_ai_mode_answers_automatically()
    {
        Assert.True(ConversationModeRules.AnswersAutomatically(ConversationModes.Ai));
        Assert.False(ConversationModeRules.AnswersAutomatically(ConversationModes.Human));
        Assert.False(ConversationModeRules.AnswersAutomatically(ConversationModes.Closed));
    }

    [Fact]
    public void A_human_handoff_moves_an_ai_conversation_to_human()
    {
        Assert.Equal(ConversationModes.Human, ConversationModeRules.AfterHandoff(ConversationModes.Ai));
    }

    [Fact]
    public void A_handoff_leaves_an_already_human_conversation_unchanged()
    {
        Assert.Equal(ConversationModes.Human, ConversationModeRules.AfterHandoff(ConversationModes.Human));
    }

    [Fact]
    public void An_explicit_release_returns_human_to_ai()
    {
        Assert.Equal(ConversationModes.Ai, ConversationModeRules.AfterExplicitRelease(ConversationModes.Human));
    }

    [Fact]
    public void An_explicit_release_never_reopens_a_closed_conversation()
    {
        Assert.Null(ConversationModeRules.AfterExplicitRelease(ConversationModes.Closed));
    }

    [Fact]
    public void Closing_moves_ai_and_human_to_closed()
    {
        Assert.Equal(ConversationModes.Closed, ConversationModeRules.AfterClose(ConversationModes.Ai));
        Assert.Equal(ConversationModes.Closed, ConversationModeRules.AfterClose(ConversationModes.Human));
        Assert.Null(ConversationModeRules.AfterClose(ConversationModes.Closed));
    }
}
