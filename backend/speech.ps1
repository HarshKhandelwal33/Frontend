param([ValidateSet('listen','speak','check')][string]$Mode = 'check', [string]$Culture = 'en-US')
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
[Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false)
[Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false)
try {
    Add-Type -AssemblyName System.Speech
    if ($Mode -eq 'check') {
        $recognizers = @([System.Speech.Recognition.SpeechRecognitionEngine]::InstalledRecognizers() | ForEach-Object { $_.Culture.Name })
        $speaker = New-Object System.Speech.Synthesis.SpeechSynthesizer
        try {
            $voices = @($speaker.GetInstalledVoices() | ForEach-Object { $_.VoiceInfo.Name })
            @{recognizers=$recognizers;voices=$voices} | ConvertTo-Json -Compress
        } finally { $speaker.Dispose() }
    } elseif ($Mode -eq 'speak') {
        $speaker = New-Object System.Speech.Synthesis.SpeechSynthesizer
        try {
            $speaker.SetOutputToDefaultAudioDevice()
            $text = [Console]::In.ReadToEnd()
            $speaker.Speak($text)
        } finally { $speaker.Dispose() }
    } else {
        $recognizer = New-Object System.Speech.Recognition.SpeechRecognitionEngine([System.Globalization.CultureInfo]::GetCultureInfo($Culture))
        try {
            $recognizer.LoadGrammar((New-Object System.Speech.Recognition.DictationGrammar))
            $recognizer.SetInputToDefaultAudioDevice()
            @{ready=$true} | ConvertTo-Json -Compress
            while ($true) {
                $result = $recognizer.Recognize([TimeSpan]::FromSeconds(2))
                if ($null -ne $result) {
                    @{text=$result.Text;confidence=$result.Confidence} | ConvertTo-Json -Compress
                }
            }
        } finally { $recognizer.Dispose() }
    }
} catch {
    @{error=$_.Exception.Message} | ConvertTo-Json -Compress
    exit 1
}
