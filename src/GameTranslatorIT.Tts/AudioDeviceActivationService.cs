namespace GameTranslatorIT.Tts;

/// <summary>Esito di un tentativo di collegamento/abilitazione di un'uscita audio.</summary>
public sealed record AudioDeviceActivationResult(bool Activated, string Message);

/// <summary>
/// Prova a rendere utilizzabile un dispositivo scelto dall'utente.
/// I dispositivi disattivati vengono abilitati tramite Device Manager (cfgmgr32).
/// I dispositivi scollegati non possono essere collegati via software: in quel caso
/// l'app apre le impostazioni audio di Windows e resta in attesa che Windows li renda attivi.
/// </summary>
public static class AudioDeviceActivationService
{
    public static async Task<AudioDeviceActivationResult> ActivateAsync(AudioOutputDevice device)
    {
        if (device.IsActive)
            return new AudioDeviceActivationResult(true, "Dispositivo già attivo.");

        var enumerator = new CoreAudioDeviceEnumerator();
        var state = enumerator.GetEndpointState(device.Id);
        if (state == 1)
            return new AudioDeviceActivationResult(true, "Dispositivo attivo.");

        var enabled = await Task.Run(() => enumerator.TryEnableEndpoint(device.Id));
        if (enabled)
        {
            await Task.Delay(700);
            state = enumerator.GetEndpointState(device.Id);
            if (state == 1)
                return new AudioDeviceActivationResult(true,
                    $"Dispositivo '{device.Name}' abilitato da Windows.");

            return new AudioDeviceActivationResult(false,
                $"Windows ha accettato la richiesta di abilitazione per '{device.Name}', ma non lo segnala ancora attivo.");
        }

        return new AudioDeviceActivationResult(false, device.Status switch
        {
            "disattivato" => $"'{device.Name}' è disattivato in Windows e non è stato possibile abilitarlo automaticamente.",
            _ => $"'{device.Name}' non è collegato: Windows non può creare l'uscita finché il dispositivo fisico non è disponibile."
        });
    }
}
