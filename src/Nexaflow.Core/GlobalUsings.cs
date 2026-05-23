// Makes Page, BreadcrumbSegment, ITabRegistration, IFeatureConfig, etc.
// available throughout Nexaflow.Core without per-file using statements.
global using Nexaflow.Features.Common;

// Disambiguate against System.Windows.Controls.Page (WPF) — the unqualified
// name "Page" in this project always refers to the shell page viewmodel.
global using Page = Nexaflow.Features.Common.Page;
