using System;
using System.Collections.Generic;

namespace GestionHoraire.Models.ViewModels
{
    public class SecurityHistoryViewModel
    {
        public IReadOnlyList<SecurityLogItemViewModel> Logs { get; set; } = Array.Empty<SecurityLogItemViewModel>();
    }

    public class SecurityLogItemViewModel
    {
        public string Action { get; set; } = "";
        public string Label { get; set; } = "";
        public string? Details { get; set; }
        public string? IpAddress { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
