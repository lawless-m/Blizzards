# Claude Code Notes for Blizzard

## Deployment

CGI binary location: `/var/www/cgi-bin/blizzard` (NOT `/usr/lib/cgi-bin/`)

Dashboard HTML location: `/var/www/html/dw/blizzard.html` (DocumentRoot is `/var/www/html/dw`)

Deploy command (run all together):
```bash
dotnet publish -c Release -o publish && \
sudo cp publish/Blizzard /var/www/cgi-bin/blizzard && \
sudo cp blizzard.html /var/www/html/dw/blizzard.html && \
sudo find /var/tmp/systemd-private-* -name "blizzard_cache*.gz" -delete
```

## Cache Locations

Apache uses systemd PrivateTmp, so cache files are in:
- `/var/tmp/systemd-private-*/tmp/blizzard_cache_*.gz`
- `/var/tmp/systemd-private-*/tmp/blizzard_shard_*.json`

To clear backtest cache:
```bash
sudo find /var/tmp/systemd-private-* -name "*blizzard_cache*cutoff*" -delete
```

To clear all caches:
```bash
sudo find /var/tmp/systemd-private-* -name "*blizzard*" -delete
```

## Testing

Test CGI locally (bypasses Apache):
```bash
sudo -u www-data GATEWAY_INTERFACE="CGI/1.1" QUERY_STRING="cutoff=2025-01" /var/www/cgi-bin/blizzard
```

Test via Apache:
```bash
curl -s "http://localhost/cgi-bin/blizzard?cutoff=2025-01" | gunzip | python3 -c "import sys,json; print(json.load(sys.stdin)['overall']['forecast']['rows'][-1])"
```
