#!/bin/sh
# nginx resolves an upstream hostname exactly once, at startup, unless a resolver
# is configured — so when the API container is recreated and gets a new IP, nginx
# keeps dialling the dead address until it is restarted itself.
#
# The container's own DNS server differs between runtimes (Docker uses 127.0.0.11,
# podman uses an aardvark-dns address on the bridge), so read it from resolv.conf
# rather than hardcoding either.
set -e

nameserver=$(awk '/^nameserver/ { print $2; exit }' /etc/resolv.conf)
[ -n "$nameserver" ] || exit 0

# IPv6 addresses have to be bracketed in the resolver directive.
case "$nameserver" in
  *:*) nameserver="[$nameserver]" ;;
esac

echo "resolver $nameserver valid=10s ipv6=off;" > /etc/nginx/conf.d/00-resolver.conf
