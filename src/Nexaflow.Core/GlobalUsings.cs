// Makes Page, BreadcrumbSegment, IPageRegistration, IFeatureConfig, etc.
// available throughout Nexaflow.Core without per-file using statements.
global using Nexaflow.Features.Common;

// Disambiguate against System.Windows.Controls.Page (WPF) — the unqualified
// name "Page" in this project always refers to the shell page viewmodel.
global using Page = Nexaflow.Features.Common.Page;

// The session, the assistant and the composition root this app adapts.
global using Nexaflow.Session;
global using Nexaflow.Session.AI;
global using Nexaflow.Session.Help;
global using Nexaflow.Session.Localization;
global using Nexaflow.Session.Models;
global using Nexaflow.Session.Services;
global using Nexaflow.Session.Services.Elevation;
global using Nexaflow.Session.Services.LocalAi;
global using Nexaflow.Session.Services.Logging;
global using Nexaflow.Session.Services.Tools;
global using Nexaflow.Assistant;
global using Nexaflow.Assistant.Agent;
global using Nexaflow.Assistant.Documents;
global using Nexaflow.Assistant.Generation;
global using Nexaflow.Assistant.ShellAi;
global using Nexaflow.Assistant.Voice;
global using Nexaflow.Host;
