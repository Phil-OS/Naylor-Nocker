namespace NaylorNocker.Core.Admission;
public sealed class FirewallShutdownException()
    : Exception("Firewall cleanup could not be confirmed within two seconds. The host must terminate the process.");
