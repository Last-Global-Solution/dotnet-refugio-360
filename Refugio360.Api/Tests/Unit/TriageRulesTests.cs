using Refugio360.Api;
using Xunit;

namespace Refugio360.Api.Tests;

public sealed class TriageRulesTests
{
    [Theory]
    [InlineData(OccurrenceStatuses.Recebida, OccurrenceStatuses.EmAnalise)]
    [InlineData(OccurrenceStatuses.EmAnalise, OccurrenceStatuses.Confirmada)]
    [InlineData(OccurrenceStatuses.EmAnalise, OccurrenceStatuses.Descartada)]
    [InlineData(OccurrenceStatuses.Confirmada, OccurrenceStatuses.Confirmada)]
    public void Expected_status_transitions_are_allowed(string current, string next) =>
        Assert.True(TriageRules.CanChangeStatus(current, next));

    [Theory]
    [InlineData(OccurrenceStatuses.Recebida, OccurrenceStatuses.Confirmada)]
    [InlineData(OccurrenceStatuses.Confirmada, OccurrenceStatuses.Recebida)]
    [InlineData(OccurrenceStatuses.Descartada, OccurrenceStatuses.EmAnalise)]
    public void Invalid_status_transitions_are_rejected(string current, string next) =>
        Assert.False(TriageRules.CanChangeStatus(current, next));
}
