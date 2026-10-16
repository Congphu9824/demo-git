$query = '{service_name="ECommerce.Api"}'
try {
    $resp = Invoke-RestMethod -Uri "http://localhost:3100/loki/api/v1/query" -Body @{ query = $query } -Method Post
    Write-Output "Status: $($resp.status)"
    Write-Output "Status: $($resp.status)"
    Write-Output "Streams: $($resp.data.result.Count)"
    foreach ($item in $resp.data.result) {
        Write-Output "Stream labels: $($item.stream | ConvertTo-Json -Compress)"
        Write-Output "Values count: $($item.values.Count)"
        if ($item.values.Count -gt 0) {
            Write-Output "Latest log: $($item.values[0][1])"
        }
    }
} catch {
    Write-Output "Error: $_"
}
