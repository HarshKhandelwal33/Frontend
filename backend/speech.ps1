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
            $commands = New-Object System.Speech.Recognition.Choices
            $commands.Add([string[]]@("I'm ready", 'I am ready', 'next step', 'go back', 'previous step', 'pause', 'resume', 'reset', 'start again', 'repeat', 'repeat the explanation'))
            $builder = New-Object System.Speech.Recognition.GrammarBuilder
            $builder.Culture = [System.Globalization.CultureInfo]::GetCultureInfo($Culture)
            $builder.Append($commands)
            $commandGrammar = New-Object System.Speech.Recognition.Grammar($builder)
            $commandGrammar.Name = 'Training commands'
            $commandGrammar.Priority = 1
            $recognizer.LoadGrammar($commandGrammar)
            # Command-only mode is more reliable for the offline demonstration.
            # Enable open-question recognition explicitly when using the AI tutor.
            if ($env:SPEECH_DICTATION -eq '1') {
                $recognizer.LoadGrammar((New-Object System.Speech.Recognition.DictationGrammar))
            }
            $recognizer.SetInputToDefaultAudioDevice()
            $levelSubscription = Register-ObjectEvent -InputObject $recognizer -EventName AudioLevelUpdated -SourceIdentifier 'TrainingMicLevel'
            @{ready=$true} | ConvertTo-Json -Compress
            while ($true) {
                $result = $recognizer.Recognize([TimeSpan]::FromSeconds(2))
                $peakLevel = 0
                foreach ($levelEvent in @(Get-Event -SourceIdentifier 'TrainingMicLevel' -ErrorAction SilentlyContinue)) {
                    $peakLevel = [Math]::Max($peakLevel, $levelEvent.SourceEventArgs.AudioLevel)
                    Remove-Event -EventIdentifier $levelEvent.EventIdentifier
                }
                @{audio_level=$peakLevel;audio_state=$recognizer.AudioState.ToString()} | ConvertTo-Json -Compress
                if ($null -ne $result) {
                    @{text=$result.Text;confidence=$result.Confidence} | ConvertTo-Json -Compress
                }
            }
        } finally {
            Unregister-Event -SourceIdentifier 'TrainingMicLevel' -ErrorAction SilentlyContinue
            $recognizer.Dispose()
        }
    }
} catch {
    @{error=$_.Exception.Message} | ConvertTo-Json -Compress
    exit 1
}
