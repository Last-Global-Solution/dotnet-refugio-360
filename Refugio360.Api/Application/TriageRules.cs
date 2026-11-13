namespace Refugio360.Api;

public static class TriageRules
{
    public static bool IsVisibleStatus(string? status) => status is
        OccurrenceStatuses.Recebida or OccurrenceStatuses.EmAnalise or
        OccurrenceStatuses.Confirmada or OccurrenceStatuses.Descartada;

    public static bool CanChangeStatus(string current, string next) =>
        current == next ||
        (current == OccurrenceStatuses.Recebida && next == OccurrenceStatuses.EmAnalise) ||
        (current == OccurrenceStatuses.EmAnalise &&
            next is OccurrenceStatuses.Confirmada or OccurrenceStatuses.Descartada);
}
