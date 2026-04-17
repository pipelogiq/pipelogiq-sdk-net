using System.Text.Json;
using PipelogiqSDK.Abstractions;

namespace PipelogiqSDK.Agent.Configuration;

internal static class AgentCriticRuntime
{
    public static AgentCriticMode ResolveMode(IStageContext? context, AgentOptions agentOptions)
        => TryGetOverrideMode(context, out var mode) ? mode : agentOptions.Critic.Mode;

    public static bool TryGetOverrideMode(IStageContext? context, out AgentCriticMode mode)
    {
        mode = AgentCriticMode.Off;

        if (context?.Payload == null ||
            !context.Payload.TryGetValue(global::PipelogiqSDK.Agent.AgentConstants.CriticMode, out var rawValue) ||
            rawValue == null)
            return false;

        switch (rawValue)
        {
            case AgentCriticMode typedMode:
                mode = typedMode;
                return true;

            case JsonElement element when TryParseJsonElement(element, out mode):
                return true;

            case string text when Enum.TryParse(text, ignoreCase: true, out AgentCriticMode parsedMode):
                mode = parsedMode;
                return true;

            case int numeric when Enum.IsDefined(typeof(AgentCriticMode), numeric):
                mode = (AgentCriticMode)numeric;
                return true;

            case long longNumeric when Enum.IsDefined(typeof(AgentCriticMode), (int)longNumeric):
                mode = (AgentCriticMode)longNumeric;
                return true;

            default:
                return false;
        }
    }

    private static bool TryParseJsonElement(JsonElement element, out AgentCriticMode mode)
    {
        mode = AgentCriticMode.Off;

        if (element.ValueKind == JsonValueKind.String &&
            Enum.TryParse(element.GetString(), ignoreCase: true, out AgentCriticMode parsedMode))
        {
            mode = parsedMode;
            return true;
        }

        if (element.ValueKind == JsonValueKind.Number &&
            element.TryGetInt32(out var numeric) &&
            Enum.IsDefined(typeof(AgentCriticMode), numeric))
        {
            mode = (AgentCriticMode)numeric;
            return true;
        }

        return false;
    }
}
