namespace Nexaflow.Features.Tabular.ViewModels;

/// <summary>One hop in the tabular view's path breadcrumb.</summary>
public sealed record BreadcrumbStep(string Label, string FullPath, bool IsFile = false);
