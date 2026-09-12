#!/bin/sh
# nginx resolves an upstream hostname exactly once, at startup, unless a resolver
# is configured — so when the API container is recreated and gets a new IP, nginx
# keeps dialling the dead address until it is restarted itself.
#
# The DNS server that knows the compose service names is the bridge's gateway:
# aardvark-dns on podman, the embedded DNS on Docker. /etc/resolv.conf can carry
# upstream or host DNS servers that do not know container aliases, so prefer the
# gateway and only fall back to resolv.conf when it is unavailable.
set -e

nameserver=$(ip route show default | awk '/default/ { print $3; exit }')

if [ -z "$nameserver" ]; then
  nameserver=$(awk '/^nameserver/ { print $2; exit }' /etc/resolv.conf)
fi

[ -n "$nameserver" ] || exit 0

# IPv6 addresses have to be bracketed in the resolver directive.
case "$nameserver" in
  *:*) nameserver="[$nameserver]" ;;
esac

echo "resolver $nameserver valid=10s ipv6=off;" > /etc/nginx/conf.d/00-resolver.conf
