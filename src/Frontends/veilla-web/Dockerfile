# syntax=docker/dockerfile:1
#
# Image d'une application front Veilla (CTR-01 à CTR-05, CTR-07).
# Une image par application, sélectionnée par l'argument APP :
#   docker build --build-arg APP=portail-employeur -t veilla/portail-employeur .
# APP ∈ { app-interne, portail-employeur, portail-travailleur }
#
# Aucune configuration d'environnement n'est compilée : assets/config.json est généré au
# démarrage à partir des variables d'environnement (ou monté), voir docker/entrypoint/.

ARG NODE_IMAGE=node:24-alpine
# Épingler par empreinte (digest) dans la CI ; image reconstruite au moins mensuellement (CTR-04).
ARG NGINX_IMAGE=nginxinc/nginx-unprivileged:stable-alpine

# --- Étape 1 : construction (outils Node absents de l'image finale, CTR-03) ---
FROM ${NODE_IMAGE} AS build
ARG APP
RUN case "$APP" in \
      app-interne|portail-employeur|portail-travailleur) ;; \
      *) echo "ARG APP invalide : '$APP' (app-interne | portail-employeur | portail-travailleur)" >&2; exit 1 ;; \
    esac
WORKDIR /src
ENV NG_CLI_ANALYTICS=false CI=true
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY angular.json tsconfig.json ./
COPY projects ./projects
RUN npx ng build "$APP" --configuration production \
 && rm -f "dist/$APP/browser/assets/config.json"

# --- Étape 2 : exécution (nginx non root, port 8080, CTR-07) ---
FROM ${NGINX_IMAGE} AS runtime
ARG APP
LABEL org.opencontainers.image.title="veilla-${APP}" \
      org.opencontainers.image.description="Front Veilla (${APP}) servi par nginx non root" \
      org.opencontainers.image.source="https://github.com/veilla/veilla"

COPY docker/nginx/default.conf /etc/nginx/conf.d/default.conf
COPY docker/nginx/security-headers.conf.template /etc/veilla/security-headers.conf.template
COPY docker/config.template.json /etc/veilla/config.template.json
COPY --chmod=0755 docker/entrypoint/40-veilla-runtime-config.sh /docker-entrypoint.d/40-veilla-runtime-config.sh
COPY --from=build /src/dist/${APP}/browser /usr/share/nginx/html

# Fichiers générés au démarrage : /tmp/veilla (monter un volume emptyDir sur /tmp si le
# système de fichiers racine est en lecture seule).
ENV VEILLA_RUNTIME_DIR=/tmp/veilla

USER 101
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s --start-period=5s \
  CMD wget -q -O /dev/null http://127.0.0.1:8080/healthz || exit 1
