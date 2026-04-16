namespace GestionHoraire.Models.ViewModels
{
    public class VerifyTwoFactorViewModel
    {
        public string ProviderLabel { get; set; } = "";
        public string Instruction { get; set; } = "";
        public string InputLabel { get; set; } = "Code de verification";
        public bool AllowResend { get; set; }
    }
}
