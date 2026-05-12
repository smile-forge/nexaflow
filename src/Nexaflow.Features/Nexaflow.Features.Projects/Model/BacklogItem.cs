using System;
using System.Collections.Generic;
using System.Text;

namespace Nexaflow.Features.Projects.Model
{
    public class BacklogItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Title { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public BacklogStatus Status { get; set; } = BacklogStatus.NotStarted;
        public string? ImpPlan { get; set; }
        public string? TestPlan { get; set; }
    }

    public enum BacklogStatus
    {
        NotStarted,
        AwaitingDesign,
        AwaitingDesignReview,
        AwaitingImplementation,
        AwaitingImplementationReview,
        AwaitingTestImplementation,
        AwaitingTestReview,
        AwaitingFinalisation,
        Cancelled
    }
}
