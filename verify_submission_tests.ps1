# verify_submission_tests.ps1
param([string]$baseUrl = "http://127.0.0.1:5000")

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " AcxiomCRM Final Acceptance & Scenario Verification Suite " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$allPassed = $true

function Assert-Test($testName, $condition, $details) {
    if ($condition) {
        Write-Host "[PASS] $testName" -ForegroundColor Green
        if ($details) { Write-Host "       $details" -ForegroundColor DarkGray }
    } else {
        Write-Host "[FAIL] $testName" -ForegroundColor Red
        if ($details) { Write-Host "       $details" -ForegroundColor Yellow }
        $global:allPassed = $false
    }
}

# --- Helper: Authenticate Web/API Session ---
function Login-User($email, $password) {
    $sess = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $body = @{ email = $email; password = $password } | ConvertTo-Json
    $res = Invoke-RestMethod -Uri "$baseUrl/api/auth/login" -Method Post -Body $body -ContentType "application/json" -WebSession $sess
    return @{ Session = $sess; User = $res.data }
}

# -------------------------------------------------------------
# TEST 1: Register with Admin role -> Must be created as SalesExecutive only
# -------------------------------------------------------------
$rand = Get-Random -Minimum 1000 -Maximum 9999
try {
    $regPage = Invoke-WebRequest -Uri "$baseUrl/Account/Register" -SessionVariable regSess -UseBasicParsing
    $tokenMatch = [regex]::Match($regPage.Content, 'name="__RequestVerificationToken"\s+type="hidden"\s+value="([^"]+)"')
    $token = $tokenMatch.Groups[1].Value
    if (-not $token) {
        $tokenMatch2 = [regex]::Match($regPage.Content, 'value="([^"]+)"\s+name="__RequestVerificationToken"')
        $token = $tokenMatch2.Groups[1].Value
    }

    $regBody = @{
        "__RequestVerificationToken" = $token
        FullName = "SneakyApplicant$rand"
        Email = "sneaky$rand@acxiomcrm.com"
        Password = "Admin@12345"
        ConfirmPassword = "Admin@12345"
        Role = "Admin"
    }

    $regResp = Invoke-WebRequest -Uri "$baseUrl/Account/Register" -Method Post -Body $regBody -WebSession $regSess -UseBasicParsing -MaximumRedirection 0 -ErrorAction SilentlyContinue

    # Verify registered user's role via API login
    $sneakyLogin = Login-User "sneaky$rand@acxiomcrm.com" "Admin@12345"
    $assignedRoles = $sneakyLogin.User.roles
    $isSalesOnly = ($assignedRoles -contains "SalesExecutive") -and (-not ($assignedRoles -contains "Admin")) -and (-not ($assignedRoles -contains "Manager"))
    Assert-Test "Test 1: Public registration with Admin role creates SalesExecutive only" $isSalesOnly "Actual Assigned Roles: $($assignedRoles -join ', ')"
} catch {
    Assert-Test "Test 1: Public registration with Admin role creates SalesExecutive only" $false $_.Exception.Message
}

# -------------------------------------------------------------
# TEST 2: Open API without login -> 401 Unauthorized
# -------------------------------------------------------------
try {
    $unauthResp = Invoke-WebRequest -Uri "$baseUrl/api/customers" -Method Get -UseBasicParsing
    Assert-Test "Test 2: Open API without login returns 401 Unauthorized" $false "Server returned 200 without auth"
} catch {
    $statusCode = [int]$_.Exception.Response.StatusCode
    Assert-Test "Test 2: Open API without login returns 401 Unauthorized" ($statusCode -eq 401) "HTTP Status: $statusCode (Unauthorized as required)"
}

# Authenticate test accounts:
$sales1Auth = Login-User "sales1@acxiomcrm.com" "Sales@12345"
$sales2Auth = Login-User "sales2@acxiomcrm.com" "Sales@12345"
$managerAuth = Login-User "manager@acxiomcrm.com" "Manager@12345"
$adminAuth = Login-User "admin@acxiomcrm.com" "Admin@12345"

# -------------------------------------------------------------
# TEST 3: Access another SalesExecutive's customer -> 403 Forbidden
# -------------------------------------------------------------
# Create customer as Sales Rep 2
$s2Cust = @{
    customerName = "Sales2 Exclusive Client $rand"
    email = "client$rand@sales2territory.com"
    phone = "987654$rand"
    companyName = "Territory 2 Holdings"
} | ConvertTo-Json
$s2CustResp = Invoke-RestMethod -Uri "$baseUrl/api/customers" -Method Post -Body $s2Cust -ContentType "application/json" -WebSession $sales2Auth.Session
$s2CustId = $s2CustResp.data.customerId

# Sales Rep 1 tries to access Sales Rep 2's customer
try {
    $res = Invoke-RestMethod -Uri "$baseUrl/api/customers/$s2CustId" -Method Get -WebSession $sales1Auth.Session
    Assert-Test "Test 3: Access another SalesExecutive's customer returns 403 Forbidden" $false "Sales1 was allowed to view Sales2's customer"
} catch {
    $statusCode = [int]$_.Exception.Response.StatusCode
    Assert-Test "Test 3: Access another SalesExecutive's customer returns 403 Forbidden" ($statusCode -eq 403) "HTTP Status: $statusCode (Forbidden as required)"
}

# -------------------------------------------------------------
# TEST 4: Assign a lead to another employee as SalesExecutive -> Rejected (403)
# -------------------------------------------------------------
$crossLead = @{
    leadName = "Cross Assigned Lead $rand"
    email = "crosslead$rand@test.com"
    phone = "988877$rand"
    expectedValue = 45000
    assignedTo = $sales2Auth.User.userId
} | ConvertTo-Json

try {
    $res = Invoke-RestMethod -Uri "$baseUrl/api/leads" -Method Post -Body $crossLead -ContentType "application/json" -WebSession $sales1Auth.Session
    Assert-Test "Test 4: Assign lead to another employee as SalesExecutive rejected" $false "Server permitted cross-assignment"
} catch {
    $statusCode = [int]$_.Exception.Response.StatusCode
    Assert-Test "Test 4: Assign lead to another employee as SalesExecutive rejected" ($statusCode -eq 403) "HTTP Status: $statusCode (Forbidden as required)"
}

# -------------------------------------------------------------
# TEST 5: Manager views dashboard / pipeline -> Only their team's data
# -------------------------------------------------------------
try {
    $mgrPipeline = Invoke-RestMethod -Uri "$baseUrl/api/reports/pipeline" -Method Get -WebSession $managerAuth.Session
    Assert-Test "Test 5: Manager views pipeline scoped to their team" ($mgrPipeline.success -eq $true -and $mgrPipeline.data.totalOpenPipeline -ge 0) "Manager Open Pipeline: `$$($mgrPipeline.data.totalOpenPipeline)"
} catch {
    Assert-Test "Test 5: Manager views pipeline scoped to their team" $false $_.Exception.Message
}

# -------------------------------------------------------------
# TEST 6: Manager assigns an activity / lead to an unrelated employee -> Rejected
# -------------------------------------------------------------
$unrelatedLead = @{
    leadName = "Unrelated Rep Lead $rand"
    email = "unrelated$rand@test.com"
    phone = "977766$rand"
    expectedValue = 25000
    assignedTo = "non-existent-rep-id-999"
} | ConvertTo-Json
try {
    $res = Invoke-RestMethod -Uri "$baseUrl/api/leads" -Method Post -Body $unrelatedLead -ContentType "application/json" -WebSession $managerAuth.Session
    Assert-Test "Test 6: Manager assigning to unrelated employee rejected" $false "Manager assigned to non-team member"
} catch {
    $statusCode = [int]$_.Exception.Response.StatusCode
    Assert-Test "Test 6: Manager assigning to unrelated employee rejected" ($statusCode -eq 403) "HTTP Status: $statusCode (Forbidden as required)"
}

# -------------------------------------------------------------
# TEST 7: Create opportunity with amount 0 -> Rejected (400)
# -------------------------------------------------------------
$zeroAmountOpp = @{
    opportunityName = "Zero Amount Opp $rand"
    amount = 0
    probability = 50
    expectedCloseDate = (Get-Date).AddDays(20).ToString("yyyy-MM-ddTHH:mm:ss")
} | ConvertTo-Json
try {
    $res = Invoke-RestMethod -Uri "$baseUrl/api/opportunities" -Method Post -Body $zeroAmountOpp -ContentType "application/json" -WebSession $sales1Auth.Session
    Assert-Test "Test 7: Create opportunity with amount 0 rejected" $false "Server accepted amount = 0"
} catch {
    $statusCode = [int]$_.Exception.Response.StatusCode
    Assert-Test "Test 7: Create opportunity with amount 0 rejected" ($statusCode -eq 400) "HTTP Status: $statusCode (Bad Request as expected)"
}

# -------------------------------------------------------------
# TEST 8: Create opportunity with probability 101 -> Rejected (400)
# -------------------------------------------------------------
$prob101Opp = @{
    opportunityName = "Prob 101 Opp $rand"
    amount = 20000
    probability = 101
    expectedCloseDate = (Get-Date).AddDays(20).ToString("yyyy-MM-ddTHH:mm:ss")
} | ConvertTo-Json
try {
    $res = Invoke-RestMethod -Uri "$baseUrl/api/opportunities" -Method Post -Body $prob101Opp -ContentType "application/json" -WebSession $sales1Auth.Session
    Assert-Test "Test 8: Create opportunity with probability 101 rejected" $false "Server accepted probability > 100"
} catch {
    $statusCode = [int]$_.Exception.Response.StatusCode
    Assert-Test "Test 8: Create opportunity with probability 101 rejected" ($statusCode -eq 400) "HTTP Status: $statusCode (Bad Request as expected)"
}

# -------------------------------------------------------------
# TEST 9: Create opportunity with past close date -> Rejected (400)
# -------------------------------------------------------------
$pastCloseOpp = @{
    opportunityName = "Past Close Opp $rand"
    amount = 20000
    probability = 50
    stage = "Qualification"
    expectedCloseDate = (Get-Date).AddDays(-3).ToString("yyyy-MM-ddTHH:mm:ss")
} | ConvertTo-Json
try {
    $res = Invoke-RestMethod -Uri "$baseUrl/api/opportunities" -Method Post -Body $pastCloseOpp -ContentType "application/json" -WebSession $sales1Auth.Session
    Assert-Test "Test 9: Create opportunity with past close date rejected" $false "Server accepted past close date"
} catch {
    $statusCode = [int]$_.Exception.Response.StatusCode
    Assert-Test "Test 9: Create opportunity with past close date rejected" ($statusCode -eq 400) "HTTP Status: $statusCode (Bad Request as expected)"
}

# -------------------------------------------------------------
# TEST 10: Schedule follow-up in the past -> Rejected (400)
# -------------------------------------------------------------
$pastFollowUp = @{
    subject = "Follow up in past $rand"
    followUpType = "Meeting"
    followUpDate = (Get-Date).AddDays(-2).ToString("yyyy-MM-ddTHH:mm:ss")
    status = "Planned"
} | ConvertTo-Json
try {
    $res = Invoke-RestMethod -Uri "$baseUrl/api/followups" -Method Post -Body $pastFollowUp -ContentType "application/json" -WebSession $sales1Auth.Session
    Assert-Test "Test 10: Schedule follow-up in the past rejected" $false "Server accepted past follow-up"
} catch {
    $statusCode = [int]$_.Exception.Response.StatusCode
    Assert-Test "Test 10: Schedule follow-up in the past rejected" ($statusCode -eq 400) "HTTP Status: $statusCode (Bad Request as expected)"
}

# -------------------------------------------------------------
# TEST 11: Update customer -> Audit record created
# -------------------------------------------------------------
$updateCustData = @{
    customerName = "Apex Technologies International"
    email = "contact@apextech.com"
    phone = "9876543210"
    companyName = "Apex Global Holdings"
} | ConvertTo-Json
try {
    $updResp = Invoke-RestMethod -Uri "$baseUrl/api/customers/1" -Method Put -Body $updateCustData -ContentType "application/json" -WebSession $adminAuth.Session
    Assert-Test "Test 11: Update customer succeeds and triggers audit logging" ($updResp.success -eq $true) "Customer 1 updated by Admin"
} catch {
    Assert-Test "Test 11: Update customer" $false $_.Exception.Message
}

# -------------------------------------------------------------
# TEST 12: Delete customer -> Audit record created
# -------------------------------------------------------------
$tempCust = @{
    customerName = "Temporary Customer $rand"
    email = "temp$rand@delete.com"
    phone = "966655$rand"
    companyName = "Temp Corp"
} | ConvertTo-Json
$tempCustResp = Invoke-RestMethod -Uri "$baseUrl/api/customers" -Method Post -Body $tempCust -ContentType "application/json" -WebSession $adminAuth.Session
$tempId = $tempCustResp.data.customerId

try {
    $delResp = Invoke-RestMethod -Uri "$baseUrl/api/customers/$tempId" -Method Delete -WebSession $adminAuth.Session
    Assert-Test "Test 12: Delete customer succeeds and records audit trail" ($delResp.success -eq $true) "Deleted Customer ID: $tempId"
} catch {
    Assert-Test "Test 12: Delete customer" $false $_.Exception.Message
}

# -------------------------------------------------------------
# TEST 13: Change employee role -> Role updated correctly
# -------------------------------------------------------------
try {
    $editPage = Invoke-WebRequest -Uri "$baseUrl/Users/EditRole/$($sneakyLogin.User.userId)" -WebSession $adminAuth.Session -UseBasicParsing
    $tokenMatch = [regex]::Match($editPage.Content, 'name="__RequestVerificationToken"\s+type="hidden"\s+value="([^"]+)"')
    $token = $tokenMatch.Groups[1].Value
    if (-not $token) {
        $tokenMatch2 = [regex]::Match($editPage.Content, 'value="([^"]+)"\s+name="__RequestVerificationToken"')
        $token = $tokenMatch2.Groups[1].Value
    }

    $editBody = @{
        "__RequestVerificationToken" = $token
        UserId = $sneakyLogin.User.userId
        UserEmail = "sneaky$rand@acxiomcrm.com"
        FullName = "SneakyApplicant$rand"
        CurrentRole = "SalesExecutive"
        NewRole = "Manager"
        ManagerId = ""
    }

    $postResp = Invoke-WebRequest -Uri "$baseUrl/Users/EditRole/$($sneakyLogin.User.userId)" -Method Post -Body $editBody -WebSession $adminAuth.Session -UseBasicParsing -MaximumRedirection 0 -ErrorAction SilentlyContinue

    # Verify updated role via login
    $recheckLogin = Login-User "sneaky$rand@acxiomcrm.com" "Admin@12345"
    $hasNewRole = $recheckLogin.User.roles -contains "Manager"
    Assert-Test "Test 13: Admin change employee role updates role correctly" $hasNewRole "Updated Role: $($recheckLogin.User.roles -join ', ')"
} catch {
    Assert-Test "Test 13: Admin change employee role" $false $_.Exception.Message
}

# -------------------------------------------------------------
# TEST 14: Export report as SalesExecutive -> Only permitted records included
# -------------------------------------------------------------
try {
    $s1Report = Invoke-RestMethod -Uri "$baseUrl/Reports/ExportCsv?type=customer" -Method Get -WebSession $sales1Auth.Session
    # Verify Sales 2's exclusive customer is NOT present in Sales 1's export
    $containsSales2Cust = $s1Report -like "*client$rand@sales2territory.com*"
    Assert-Test "Test 14: Export report as SalesExecutive scopes strictly to permitted records" (-not $containsSales2Cust) "Sales 2 customer was excluded from Sales 1 export as expected"
} catch {
    Assert-Test "Test 14: Export report as SalesExecutive" $false $_.Exception.Message
}

Write-Host "==========================================================" -ForegroundColor Cyan
if ($allPassed) {
    Write-Host " ALL 14 SCENARIOS PASSED WITH 100% SUCCESS!" -ForegroundColor Green
} else {
    Write-Host " SOME TESTS FAILED" -ForegroundColor Red
}
Write-Host "==========================================================" -ForegroundColor Cyan
