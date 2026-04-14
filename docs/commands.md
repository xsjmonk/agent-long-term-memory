* Test embedding api
```
& {
	$ErrorActionPreference = 'Stop'
	$queryText = 'test connection to embedding api'
	$uri = 'http://127.0.0.1:8777/embed-query'

	$body = @{
		schema_version = '1.1'
		request_id     = "test-$([guid]::NewGuid().ToString())"
		task_id        = 'local-test'
		caller         = 'mcp'
		purpose        = 'direct_search'
		items          = @(
			@{
				item_id             = 'i1'
				query_kind          = 'Summary'
				retrieval_role_hint = 'Summary'
				text                = $queryText
			}
		)
	} | ConvertTo-Json -Depth 10

	try {
		$r = Invoke-RestMethod `
			-Method Post `
			-Uri $uri `
			-ContentType 'application/json' `
			-Body $body

		Write-Host "SUCCESS: API is responding" -ForegroundColor Green
		Write-Host "Provider: $($r.provider)"
		Write-Host "Model: $($r.model_name)"
		Write-Host "Dimension: $($r.dimension)"
		Write-Host "FallbackMode: $($r.fallback_mode)"
		Write-Host "Items Returned: $($r.items.Count)"

		if ($r.items.Count -gt 0 -and $r.items[0].vector.Count -gt 0) {
			Write-Host "Vector Length: $($r.items[0].vector.Count)"
			Write-Host "First 5 Values: $((($r.items[0].vector | Select-Object -First 5) -join ', '))"
		}
		else {
			Write-Host "WARNING: Response received but vector missing" -ForegroundColor Yellow
		}
	}
	catch {
		Write-Host "FAILED: API test failed" -ForegroundColor Red
		Write-Host $_.Exception.Message
	}
}
```

