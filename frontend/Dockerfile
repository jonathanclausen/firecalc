FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM node:24-alpine
WORKDIR /app
# The SSR build bundles its server dependencies, so only dist/ is needed at runtime.
COPY --from=build /app/dist ./dist
ENV NODE_ENV=production PORT=4000
EXPOSE 4000
USER node
CMD ["node", "dist/frontend/server/server.mjs"]
