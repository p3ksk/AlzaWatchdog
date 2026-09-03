# Build from the repository root:
#   podman build -f frontend/alza-watchdog-web/Dockerfile -t alza-watchdog-web .
FROM node:22-alpine AS build
WORKDIR /src

COPY frontend/alza-watchdog-web/package*.json ./
RUN npm ci

COPY frontend/alza-watchdog-web/ ./
RUN npm run build

FROM nginx:1.27-alpine AS runtime
COPY frontend/alza-watchdog-web/nginx.conf /etc/nginx/conf.d/default.conf
# The base image runs everything in this directory before starting nginx.
COPY frontend/alza-watchdog-web/docker-resolver.sh /docker-entrypoint.d/10-resolver.sh
RUN chmod +x /docker-entrypoint.d/10-resolver.sh
COPY --from=build /src/dist/alza-watchdog-web/browser /usr/share/nginx/html
EXPOSE 80
