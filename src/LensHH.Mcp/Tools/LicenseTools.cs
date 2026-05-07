using System.ComponentModel;
using LensHH.Core.Activation;
using ModelContextProtocol.Server;

namespace LensHH.Mcp.Tools
{
    [McpServerToolType]
    public class LicenseTools
    {
        [McpServerTool, Description("Get the current license and trial status including activation state, trial days remaining, and machine ID")]
        public string LicenseStatus()
        {
            bool activated = ActivationManager.IsActivated;
            bool trialActive = TrialClock.IsTrialActive;
            bool trialExpired = TrialClock.IsTrialExpired;
            int daysRemaining = TrialClock.DaysRemaining;
            string machineId = ActivationManager.GetMachineFingerprint();

            string status;
            if (activated && !trialActive)
                status = "Activated (full license)";
            else if (trialActive)
                status = $"Trial ({daysRemaining} days remaining)";
            else if (trialExpired)
                status = "Trial expired";
            else
                status = "Not activated";

            return $"License status: {status}\nMachine ID: {machineId}";
        }
    }
}
