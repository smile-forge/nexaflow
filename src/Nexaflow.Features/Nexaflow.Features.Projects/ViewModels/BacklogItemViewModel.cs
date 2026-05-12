using Nexaflow.Features.Projects.Model;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Media;

namespace Nexaflow.Features.Projects.ViewModels
{
    // ── Editable backlog item ─────────────────────────────────────────────────────

    public partial class BacklogItemViewModel : ObservableObject
    {
        private readonly ProjectDetailViewModel _parent;

        public Guid Id { get; }

        [ObservableProperty] private string _title = string.Empty;
        [ObservableProperty] private string? _notes;
        [ObservableProperty] private string? _impPlan;
        [ObservableProperty] private string? _testPlan;
        [ObservableProperty] private BacklogStatus _status;

        public string StatusLabel => Status.ToString();
        public bool IsCancelled => Status == BacklogStatus.Cancelled;
        public bool CanProgress => Status != BacklogStatus.AwaitingFinalisation && !IsCancelled;

        public Brush StatusBrush => Status switch
        {
            BacklogStatus.NotStarted => ProjectsViewModel.BrushNotStarted,
            BacklogStatus.AwaitingFinalisation => ProjectsViewModel.BrushDone,
            BacklogStatus.Cancelled => ProjectsViewModel.BrushCancelled,
            _ => ProjectsViewModel.BrushInProgress
        };

        public BacklogItemViewModel(BacklogItem item, ProjectDetailViewModel parent)
        {
            Id = item.Id;
            _title = item.Title;
            _notes = item.Notes;
            _impPlan = item.ImpPlan;
            _testPlan = item.TestPlan;
            _status = item.Status;
            _parent = parent;
        }

        partial void OnTitleChanged(string value) { /* saved explicitly */ }
        partial void OnNotesChanged(string? value) { /* saved explicitly */ }
        partial void OnImpPlanChanged(string? value) { /* saved explicitly */ }
        partial void OnTestPlanChanged(string? value) { /* saved explicitly */ }

        [RelayCommand(CanExecute = nameof(CanProgress))]
        private void Progress()
        {
            _parent.ProgressItem(this);
            OnPropertyChanged(nameof(StatusLabel));
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(IsCancelled));
            OnPropertyChanged(nameof(CanProgress));
            ProgressCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand]
        private void Cancel() => _parent.CancelItem(this);
    }
}
