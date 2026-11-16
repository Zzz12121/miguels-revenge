param([string]$Dir)
Add-Type -AssemblyName System.Speech
$s = New-Object System.Speech.Synthesis.SpeechSynthesizer
$s.SelectVoice("Microsoft Zira Desktop")
$fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(16000, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Mono)
$n = 0
foreach ($line in [System.IO.File]::ReadAllLines("$Dir\tts_jobs.tsv", [System.Text.Encoding]::UTF8)) {
    if ($line.Length -eq 0) { continue }
    $parts = $line.Split("`t", 2)
    $s.SetOutputToWaveFile("$Dir\tts_raw\$($parts[0]).wav", $fmt)
    $s.SpeakSsml($parts[1])
    $n++
}
$s.SetOutputToNull()
"spoken $n"
