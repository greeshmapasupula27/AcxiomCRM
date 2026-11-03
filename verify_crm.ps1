# verify_crm.ps1
param([string]$baseUrl = "http://127.0.0.1:5000")

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " AcxiomCRM Acceptance & Verification Test Suite (PDF 17.19)" -ForegroundColor Cyan
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

# 1. Unauthenticated Web Navigation (Scenario 1)
try {
    $req = [System.Net.WebRequest]::Create("$baseUrl/Dashboard")
    $req.AllowAutoRedirect = $false
    $resp = $req.GetResponse()
    $status = [int]$resp.StatusCode
    $loc = $resp.Headers["Location"]
    $resp.Close()
    Assert-Test "Scenario 1: Protected MVC views redirect to /Account/Login" ($status -eq 302 -and $loc -like "*/Account/Login*") "Status: $status, Redirect: $loc"
} catch [System.Net.WebException] {
    $resp = $_.Exception.Response
    if ($resp) {
        $status = [int]$resp.StatusCode
        $loc = $resp.Headers["Location"]
        $resp.Close()
        Assert-Test "Scenario 1: Protected MVC views redirect to /Account/Login" ($status -eq 302 -and $loc -like "*/Account/Login*") "Status: $status, Redirect: $loc"
    } else {
        Assert-Test "Scenario 1: Protected MVC views redirect to /Account/Login" $false $_.Exception.Message
    }
}

# 2. REST API Security: Unauthenticated request returns 401 Unauthorized (PDF 10.1 & 17.14)
try {
    $res = Invoke-WebRequest -Uri "$baseUrl/api/customers" -Method Get -UseBasicParsing
    Assert-Test "Scenario 13a: Unauthenticated API access blocked" $false "Server returned 200 without auth"
} catch {
    $statusCode = [int]$_.Exception.Response.StatusCode
    Assert-Test "Scenario 13a: Unauthenticated API access returns 401 Unauthorized" ($statusCode -eq 401) "HTTP Status: $statusCode (Unauthorized as required)"
}

# 3. REST API Authentication: POST /api/auth/login
$session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
$loginPayload = @{
    email = "sales1@acxiomcrm.com"
    password = "Sales@12345"
} | ConvertTo-Json

try {
    $loginResp = Invoke-RestMethod -Uri "$baseUrl/api/auth/login" -Method Post -Body $loginPayload -ContentType "application/json" -WebSession $session
    Assert-Test "Scenario 10: Authenticate via POST /api/auth/login" ($loginResp.success -eq $true -and $loginResp.data.email -eq "sales1@acxiomcrm.com") "Logged in user: $($loginResp.data.fullName)"
} catch {
    Assert-Test "Scenario 10: Authenticate via POST /api/auth/login" $false $_.Exception.Message
}

# 4. Authenticated REST API: GET /api/customers
try {
    $custResp = Invoke-RestMethod -Uri "$baseUrl/api/customers" -Method Get -WebSession $session
    Assert-Test "Scenario 13b: GET /api/customers returns valid JSON" ($custResp.success -eq $true -and $custResp.data.Count -gt 0) "Found $($custResp.data.Count) customer(s)"
} catch {
    Assert-Test "Scenario 13b: GET /api/customers" $false $_.Exception.Message
}

# 5. Authenticated REST API: GET /api/leads
try {
    $leadsResp = Invoke-RestMethod -Uri "$baseUrl/api/leads" -Method Get -WebSession $session
    Assert-Test "Scenario 13c: GET /api/leads returns valid JSON" ($leadsResp.success -eq $true -and $leadsResp.data.Count -gt 0) "Found $($leadsResp.data.Count) lead(s)"
} catch {
    Assert-Test "Scenario 13c: GET /api/leads" $false $_.Exception.Message
}

# 6. Authenticated REST API: GET /api/opportunities
try {
    $oppResp = Invoke-RestMethod -Uri "$baseUrl/api/opportunities" -Method Get -WebSession $session
    Assert-Test "Scenario 13d: GET /api/opportunities returns valid JSON" ($oppResp.success -eq $true -and $oppResp.data.Count -gt 0) "Found $($oppResp.data.Count) opportunity deal(s)"
} catch {
    Assert-Test "Scenario 13d: GET /api/opportunities" $false $_.Exception.Message
}

# 7. Authenticated REST API: GET /api/reports/pipeline
try {
    $pipeResp = Invoke-RestMethod -Uri "$baseUrl/api/reports/pipeline" -Method Get -WebSession $session
    Assert-Test "Scenario 13e: GET /api/reports/pipeline returns valid JSON" ($pipeResp.success -eq $true -and $pipeResp.data.totalOpenPipeline -gt 0) "Total Open Pipeline: `$$($pipeResp.data.totalOpenPipeline)"
} catch {
    Assert-Test "Scenario 13e: GET /api/reports/pipeline" $false $_.Exception.Message
}

# 8. Business Validation: Opportunity Amount <= 0 (Scenario 5)
$invalidAmountOpp = @{
    opportunityName = "Invalid Amount Deal"
    amount = 0
    probability = 50
    expectedCloseDate = (Get-Date).AddDays(10).ToString("yyyy-MM-ddTHH:mm:ss")
} | ConvertTo-Json

try {
    $res = Invoke-RestMethod -Uri "$baseUrl/api/opportunities" -Method Post -Body $invalidAmountOpp -ContentType "application/json" -WebSession $session
    Assert-Test "Scenario 5: Opportunity with Amount <= 0 rejected" $false "Server accepted amount = 0"
} catch {
    $statusCode = [int]$_.Exception.Response.StatusCode
    Assert-Test "Scenario 5: Opportunity with Amount <= 0 rejected" ($statusCode -eq 400) "HTTP Status: $statusCode (Bad Request as expected)"
}

# 9. Business Validation: Probability > 100 (Scenario 6)
$invalidProbOpp = @{
    opportunityName = "Invalid Probability Deal"
    amount = 50000
    probability = 150
    expectedCloseDate = (Get-Date).AddDays(10).ToString("yyyy-MM-ddTHH:mm:ss")
} | ConvertTo-Json

try {
    $res = Invoke-RestMethod -Uri "$baseUrl/api/opportunities" -Method Post -Body $invalidProbOpp -ContentType "application/json" -WebSession $session
    Assert-Test "Scenario 6: Opportunity with Probability > 100 rejected" $false "Server accepted probability > 100"
} catch {
    $statusCode = [int]$_.Exception.Response.StatusCode
    Assert-Test "Scenario 6: Opportunity with Probability > 100 rejected" ($statusCode -eq 400) "HTTP Status: $statusCode (Bad Request as expected)"
}

# 10. Business Validation: Expected Close Date in Past (Scenario 7)
$pastCloseOpp = @{
    opportunityName = "Past Close Date Deal"
    amount = 50000
    probability = 50
    stage = "Proposal"
    expectedCloseDate = (Get-Date).AddDays(-5).ToString("yyyy-MM-ddTHH:mm:ss")
} | ConvertTo-Json

try {
    $res = Invoke-RestMethod -Uri "$baseUrl/api/opportunities" -Method Post -Body $pastCloseOpp -ContentType "application/json" -WebSession $session
    Assert-Test "Scenario 7: Opportunity with past Expected Close Date rejected" $false "Server accepted past date"
} catch {
    $statusCode = [int]$_.Exception.Response.StatusCode
    Assert-Test "Scenario 7: Opportunity with past Expected Close Date rejected" ($statusCode -eq 400) "HTTP Status: $statusCode (Bad Request as expected)"
}

# 11. Business Validation: Follow-Up Date in Past for New Activity (Scenario 8)
$pastFollowUp = @{
    subject = "Follow-up yesterday"
    followUpType = "Call"
    followUpDate = (Get-Date).AddDays(-2).ToString("yyyy-MM-ddTHH:mm:ss")
    status = "Planned"
} | ConvertTo-Json

try {
    $res = Invoke-RestMethod -Uri "$baseUrl/api/followups" -Method Post -Body $pastFollowUp -ContentType "application/json" -WebSession $session
    Assert-Test "Scenario 8: Follow-up dated before today rejected" $false "Server accepted past follow-up"
} catch {
    $statusCode = [int]$_.Exception.Response.StatusCode
    Assert-Test "Scenario 8: Follow-up dated before today rejected" ($statusCode -eq 400) "HTTP Status: $statusCode (Bad Request as expected)"
}

# 12. Business Validation: Duplicate Customer Email (Scenario 4)
$dupCustomer = @{
    customerName = "Duplicate Test Corp"
    email = "contact@apextech.com"
    phone = "9998887776"
} | ConvertTo-Json

try {
    $res = Invoke-RestMethod -Uri "$baseUrl/api/customers" -Method Post -Body $dupCustomer -ContentType "application/json" -WebSession $session
    Assert-Test "Scenario 4: Duplicate Customer Email rejected" $false "Server accepted duplicate customer email"
} catch {
    $statusCode = [int]$_.Exception.Response.StatusCode
    Assert-Test "Scenario 4: Duplicate Customer Email rejected" ($statusCode -eq 409) "HTTP Status: $statusCode (Conflict as expected)"
}

# 13. Valid Opportunity Creation via API & Derive Weighted Pipeline (Amount * Probability / 100)
$validOpp = @{
    opportunityName = "Acxiom Cloud Analytics Expansion"
    amount = 80000
    probability = 50
    stage = "Proposal"
    expectedCloseDate = (Get-Date).AddDays(45).ToString("yyyy-MM-ddTHH:mm:ss")
} | ConvertTo-Json

try {
    $res = Invoke-RestMethod -Uri "$baseUrl/api/opportunities" -Method Post -Body $validOpp -ContentType "application/json" -WebSession $session
    $weighted = $res.data.weightedAmount
    Assert-Test "Valid Opportunity Creation & Weighted Calculation" ($res.success -eq $true -and $weighted -eq 40000) "Created ID $($res.data.opportunityId), Weighted: `$$weighted"
} catch {
    Assert-Test "Valid Opportunity Creation" $false $_.Exception.Message
}

# 14. Role Assignment Security: SalesExecutive attempting to assign a lead to another user ID
$crossAssignLead = @{
    leadName = "Illegitimate Cross Assigned Lead"
    email = "cross@test.com"
    phone = "9876543210"
    expectedValue = 50000
    assignedTo = "admin-or-other-user-guid-999"
} | ConvertTo-Json

try {
    $res = Invoke-RestMethod -Uri "$baseUrl/api/leads" -Method Post -Body $crossAssignLead -ContentType "application/json" -WebSession $session
    Assert-Test "Security: SalesExecutive prevented from assigning record to other users" $false "Server allowed cross-user assignment"
} catch {
    $statusCode = [int]$_.Exception.Response.StatusCode
    Assert-Test "Security: SalesExecutive prevented from assigning record to other users" ($statusCode -eq 403) "HTTP Status: $statusCode (Forbidden as required)"
}

# 15. Ownership Enforcement: SalesExecutive attempting to create Opportunity with inaccessible Customer ID
$inaccessibleCustomerOpp = @{
    opportunityName = "Cross-tenant deal attempt"
    customerId = 99999
    amount = 30000
    probability = 40
    expectedCloseDate = (Get-Date).AddDays(30).ToString("yyyy-MM-ddTHH:mm:ss")
} | ConvertTo-Json

try {
    $res = Invoke-RestMethod -Uri "$baseUrl/api/opportunities" -Method Post -Body $inaccessibleCustomerOpp -ContentType "application/json" -WebSession $session
    Assert-Test "Security: Inaccessible foreign customer reference rejected" $false "Server accepted unauthorized customer link"
} catch {
    $statusCode = [int]$_.Exception.Response.StatusCode
    Assert-Test "Security: Inaccessible foreign customer reference rejected" ($statusCode -eq 403) "HTTP Status: $statusCode (Forbidden as required)"
}

Write-Host "==========================================================" -ForegroundColor Cyan
if ($allPassed) {
    Write-Host " ALL ACCEPTANCE SCENARIOS PASSED WITH 100% SUCCESS!" -ForegroundColor Green
} else {
    Write-Host " SOME TESTS FAILED" -ForegroundColor Red
}
Write-Host "==========================================================" -ForegroundColor Cyan
