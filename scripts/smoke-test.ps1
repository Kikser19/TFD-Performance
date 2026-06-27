# End-to-end smoke test for the running stack.
# Prereq: `docker compose up -d` (gateway listening on http://localhost:8080).
# Run:    pwsh ./scripts/smoke-test.ps1   (or: powershell -File scripts\smoke-test.ps1)
#
# Exercises the full 1:1 coaching flow through the gateway and asserts the key
# access-control rules. Prints PASS/FAIL per check and exits non-zero on any failure.

$ErrorActionPreference = "Stop"
$base = $env:GATEWAY_URL; if (-not $base) { $base = "http://localhost:8080" }
$fail = 0

function J($o) { $o | ConvertTo-Json -Depth 8 -Compress }
function Pass($m) { Write-Host "  PASS  $m" -ForegroundColor Green }
function Fail($m) { Write-Host "  FAIL  $m" -ForegroundColor Red; $script:fail++ }

# Call helper: returns the parsed body; throws on non-2xx (caller decides if that's expected).
function Api($method, $path, $body, $token) {
    $headers = @{}
    if ($token) { $headers.Authorization = "Bearer $token" }
    $args = @{ Method = $method; Uri = "$base$path"; Headers = $headers }
    if ($null -ne $body) { $args.Body = (J $body); $args.ContentType = "application/json" }
    Invoke-RestMethod @args
}

# Asserts a call FAILS with a specific HTTP status.
function ExpectStatus($expected, $label, $script) {
    try { & $script | Out-Null; Fail "$label (expected HTTP $expected, got success)" }
    catch {
        $code = $_.Exception.Response.StatusCode.value__
        if ($code -eq $expected) { Pass "$label (HTTP $code)" }
        else { Fail "$label (expected HTTP $expected, got $code)" }
    }
}

Write-Host "`nSmoke testing $base`n"

# --- Liveness ---
try { $h = Api GET "/health"; if ($h.status -eq "healthy") { Pass "gateway /health" } else { Fail "gateway /health unexpected: $(J $h)" } }
catch { Fail "gateway /health unreachable - is the stack up? ($($_.Exception.Message))"; exit 1 }

$ts = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$trainerEmail = "smoke-trainer-$ts@x.com"
$clientEmail  = "smoke-client-$ts@x.com"
$outsiderEmail = "smoke-outsider-$ts@x.com"

# --- Accounts ---
$trainer = Api POST "/api/auth/signup" @{ name="Coach"; email=$trainerEmail; password="supersecret1"; role="trainer" }
$client  = Api POST "/api/auth/signup" @{ name="Client"; email=$clientEmail; password="supersecret1"; role="client" }
$outsider= Api POST "/api/auth/signup" @{ name="Outsider"; email=$outsiderEmail; password="supersecret1"; role="client" }
$tT=$trainer.token; $tC=$client.token; $tO=$outsider.token
if ($tT -and $tC) { Pass "signup trainer + client returned JWTs" } else { Fail "signup did not return tokens" }

# --- Login + /me round trip ---
$login = Api POST "/api/auth/login" @{ email=$trainerEmail; password="supersecret1" }
$me = Api GET "/api/auth/me" $null $login.token
if ($me.email -eq $trainerEmail -and $me.role -eq "trainer") { Pass "login + /me (gateway validates JWT, forwards identity)" }
else { Fail "/me mismatch: $(J $me)" }

# --- Trainer builds a program ---
$ex = Api POST "/api/exercises" @{ name="Back Squat"; youtubeVideoId="dQw4w9WgXcQ"; instructions="Depth below parallel." } $tT
$prog = Api POST "/api/programs" @{ title="Strength Foundations"; type="one_on_one"; priceCents=$null } $tT
$wk = Api POST "/api/programs/$($prog.id)/workouts" @{ title="Day 1 - Lower"; orderIndex=0 } $tT
$we = Api POST "/api/workouts/$($wk.id)/exercises" @{ exerciseId=$ex.id; prescribedSets=5; prescribedReps=5; prescribedRestSeconds=120; orderIndex=0 } $tT
if ($prog.status -eq "draft" -and $we.exerciseName -eq "Back Squat") { Pass "trainer built exercise/program/workout/prescription" }
else { Fail "build chain unexpected" }

# --- Trainer assigns to client (manual 1:1) ---
$assign = Api POST "/api/programs/$($prog.id)/assignments" @{ clientId=$client.user.id } $tT
if ($assign.source -eq "manual") { Pass "manual assignment created" } else { Fail "assignment source: $($assign.source)" }

# --- Client sees the assigned content ---
$myPrograms = Api GET "/api/me/programs" $null $tC
if ($myPrograms.total -ge 1 -and $myPrograms.items[0].id -eq $prog.id) { Pass "client sees assigned program in /api/me/programs" }
else { Fail "client my-programs: $(J $myPrograms)" }
$cEx = Api GET "/api/workouts/$($wk.id)/exercises" $null $tC
if ($cEx[0].youtubeVideoId -eq "dQw4w9WgXcQ" -and $cEx[0].prescribedSets -eq 5) { Pass "client sees prescription with video id" }
else { Fail "client prescription view: $(J $cEx)" }

# --- Tracking: client logs sets/reps + nutrition; trainer dashboard reads them ---
$weId = $cEx[0].id
$log = Api POST "/api/workout-logs" @{ workoutId=$wk.id; sets=@(
    @{ workoutExerciseId=$weId; setNumber=1; repsCompleted=5; weightUsed=100.0 },
    @{ workoutExerciseId=$weId; setNumber=2; repsCompleted=5; weightUsed=102.5 }) } $tC
if ($log.sets.Count -eq 2) { Pass "client logged a workout (access verified via Program)" } else { Fail "workout log: $(J $log)" }

$myLogs = Api GET "/api/workout-logs" $null $tC
if ($myLogs.total -ge 1) { Pass "client reads own workout history" } else { Fail "workout history: $(J $myLogs)" }

$nut = Api POST "/api/nutrition-entries" @{ foodName="Oats"; calories=300; proteinG=10.0; carbsG=54.0; fatG=5.0 } $tC
$myNut = Api GET "/api/nutrition-entries" $null $tC
if ($myNut.total -ge 1 -and $nut.foodName -eq "Oats") { Pass "client logs + reads nutrition" } else { Fail "nutrition: $(J $myNut)" }

$dashLogs = Api GET "/api/clients/$($client.user.id)/workout-logs" $null $tT
if ($dashLogs.total -ge 1) { Pass "coaching trainer reads client's logs (cross-service coaching check)" } else { Fail "trainer dash: $(J $dashLogs)" }

# --- Access control / security negatives ---
ExpectStatus 403 "client cannot create exercises"      { Api POST "/api/exercises" @{ name="x"; youtubeVideoId="y" } $tO }
ExpectStatus 403 "unassigned client cannot view program" { Api GET "/api/programs/$($prog.id)" $null $tO }
ExpectStatus 403 "client cannot assign programs"        { Api POST "/api/programs/$($prog.id)/assignments" @{ clientId=$outsider.user.id } $tO }
ExpectStatus 401 "no token is rejected at gateway"      { Api GET "/api/programs" }
ExpectStatus 401 "garbage token is rejected"            { Api GET "/api/auth/me" $null "not.a.real.token" }
ExpectStatus 409 "duplicate signup conflicts"           { Api POST "/api/auth/signup" @{ name="dup"; email=$trainerEmail; password="supersecret1"; role="trainer" } }
ExpectStatus 400 "online program requires a price"      { Api POST "/api/programs" @{ title="bad"; type="online"; priceCents=$null } $tT }

# Spoofed identity header without a valid token must NOT be honored.
ExpectStatus 401 "spoofed X-User-* header is stripped" {
    Invoke-RestMethod "$base/api/exercises" -Method Post -ContentType application/json `
        -Headers @{ "X-User-Id"=$trainer.user.id; "X-User-Role"="trainer" } -Body (J @{ name="x"; youtubeVideoId="y" })
}

Write-Host ""
if ($fail -eq 0) { Write-Host "ALL CHECKS PASSED" -ForegroundColor Green; exit 0 }
else { Write-Host "$fail CHECK(S) FAILED" -ForegroundColor Red; exit 1 }
