# syntax=docker/dockerfile:1.7

# Development-mode image: runs the Vite dev server with HMR. For a
# production build, swap to a multi-stage `npm run build` → Nginx image.
FROM node:20-alpine

WORKDIR /app

# Install dependencies in a cacheable layer.
COPY package.json package-lock.json* ./
RUN npm install --no-audit --no-fund

# Source is bind-mounted by docker-compose for live reload, so the COPY here
# is only used when building/running the image standalone.
COPY . .

EXPOSE 5173

# --host 0.0.0.0 makes Vite reachable from the host machine when the port
# is published by docker-compose.
CMD ["npm", "run", "dev", "--", "--host", "0.0.0.0"]
