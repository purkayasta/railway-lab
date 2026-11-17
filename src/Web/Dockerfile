FROM node:26-alpine AS build

WORKDIR /workspace
COPY src/Web/package.json src/Web/package-lock.json ./
RUN npm ci

COPY src/Web/ ./
RUN npm run build

FROM nginxinc/nginx-unprivileged:1.29-alpine
LABEL org.railway-lab.project="railway-lab" org.railway-lab.service="web"

COPY --from=build /workspace/dist/railway-web/browser /usr/share/nginx/html
COPY src/Web/nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 8080
