namespace NaylorNocker.Windows.Filtering;

internal interface IGrantPolicySession : IDisposable
{
    void EnableAdmission();
    void DisableAdmission();
}
