namespace Refugio360.Api;

public static class RequestValidator
{
    public static Dictionary<string, string[]> ValidateOccurrence(CreateOccurrenceRequest request) =>
        ValidateOccurrenceFields(request.Categoria, request.Descricao, request.Latitude, request.Longitude);

    public static Dictionary<string, string[]> ValidateOccurrence(UpdateOccurrenceRequest request) =>
        ValidateOccurrenceFields(request.Categoria, request.Descricao, request.Latitude, request.Longitude);

    private static Dictionary<string, string[]> ValidateOccurrenceFields(string categoria, string descricao, double latitude, double longitude)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(categoria) || categoria.Length > 60)
            errors["categoria"] = ["Informe uma categoria com até 60 caracteres."];
        if (string.IsNullOrWhiteSpace(descricao) || descricao.Length > 1000)
            errors["descricao"] = ["Informe uma descrição com até 1000 caracteres."];
        if (!ValidCoordinates(latitude, longitude))
            errors["coordenadas"] = ["Latitude ou longitude fora do intervalo válido."];
        return errors;
    }

    public static Dictionary<string, string[]> ValidateSafeLocation(CreateSafeLocationRequest request) =>
        ValidateSafeLocationFields(request.Nome, request.Endereco, request.Latitude, request.Longitude, request.Disponibilidade);

    public static Dictionary<string, string[]> ValidateSafeLocation(UpdateSafeLocationRequest request) =>
        ValidateSafeLocationFields(request.Nome, request.Endereco, request.Latitude, request.Longitude, request.Disponibilidade);

    private static Dictionary<string, string[]> ValidateSafeLocationFields(string nome, string endereco, double latitude, double longitude, string disponibilidade)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(nome) || nome.Length > 120)
            errors["nome"] = ["Informe um nome com até 120 caracteres."];
        if (string.IsNullOrWhiteSpace(endereco) || endereco.Length > 240)
            errors["endereco"] = ["Informe um endereço com até 240 caracteres."];
        if (string.IsNullOrWhiteSpace(disponibilidade) || disponibilidade.Length > 120)
            errors["disponibilidade"] = ["Informe a disponibilidade com até 120 caracteres."];
        if (!ValidCoordinates(latitude, longitude))
            errors["coordenadas"] = ["Latitude ou longitude fora do intervalo válido."];
        return errors;
    }

    public static Dictionary<string, string[]> ValidateAvailability(ConfirmAvailabilityRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Disponibilidade) || request.Disponibilidade.Length > 120)
            errors["disponibilidade"] = ["Informe a disponibilidade com até 120 caracteres."];
        return errors;
    }

    public static Dictionary<string, string[]> ValidateAlert(CreateAlertRequest request) =>
        ValidateAlertFields(request.Titulo, request.Mensagem, request.Fonte, request.ExpiraEmUtc);

    public static Dictionary<string, string[]> ValidateAlert(UpdateAlertRequest request) =>
        ValidateAlertFields(request.Titulo, request.Mensagem, request.Fonte, request.ExpiraEmUtc);

    private static Dictionary<string, string[]> ValidateAlertFields(string titulo, string mensagem, string fonte, DateTime expiraEmUtc)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(titulo) || titulo.Length > 120)
            errors["titulo"] = ["Informe um título com até 120 caracteres."];
        if (string.IsNullOrWhiteSpace(mensagem) || mensagem.Length > 1000)
            errors["mensagem"] = ["Informe uma mensagem com até 1000 caracteres."];
        if (string.IsNullOrWhiteSpace(fonte) || fonte.Length > 240)
            errors["fonte"] = ["Informe a fonte com até 240 caracteres."];
        if (expiraEmUtc <= DateTime.UtcNow)
            errors["expiraEmUtc"] = ["A validade do alerta deve estar no futuro."];
        return errors;
    }

    public static bool ValidCoordinates(double latitude, double longitude) =>
        latitude is >= -90 and <= 90 && longitude is >= -180 and <= 180;
}
