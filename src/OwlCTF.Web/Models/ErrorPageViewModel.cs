namespace OwlCTF.Models;

public sealed record ErrorPageViewModel(
    int StatusCode,
    string Title,
    string Message,
    string? RequestId);
