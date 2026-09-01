# BuildVision AI

AI construction design assistant: upload a partial building image, select an area, describe the change, and generate design variations.

## Stack

- **Frontend:** Angular 20
- **Backend:** ASP.NET Core (.NET 10)
- **AI:** OpenAI Images Edit API (`dall-e-2`) with local **demo mode** when no API key is set
- **Storage:** local `App_Data` (images + JSON history)

## Quick start (local)

### 1. Backend

```bash
cd backend/BuildVision.Api
dotnet run --launch-profile http
```

API: `http://localhost:5063`

Optional — set your OpenAI key in `appsettings.Development.json`:

```json
"OpenAI": {
  "ApiKey": "sk-..."
}
```

### 2. Frontend (dev)

```bash
cd frontend
npm start
```

App: `http://localhost:4200`

## Deploy for free (Render)

The app runs as **one service** (API serves the Angular UI from `wwwroot`).

1. Push this repo to GitHub.
2. Go to [Render](https://render.com/) → **New** → **Blueprint**.
3. Select the repo (uses `render.yaml` + `Dockerfile`).
4. Open the Render URL after deploy.
5. Optional: set env var `OpenAI__ApiKey` for live AI.

Free Render services sleep when idle; first request after sleep can take ~30–60s.

### Combined local build

```bash
cd frontend
npm run build -- --configuration=production
# copy dist/frontend/browser/* → backend/BuildVision.Api/wwwroot/
cd ../backend/BuildVision.Api
dotnet run --launch-profile http
```

Then open `http://localhost:5063`.

## MVP flow

1. Upload a construction / house image  
2. Drag to select the region to redesign  
3. Enter a prompt  
4. Generate 1–4 design variations  
5. Compare with the slider  

## API

| Method | Path | Description |
|--------|------|-------------|
| GET | `/api/health` | Health + AI mode |
| GET | `/api/designs` | Design history |
| GET | `/api/designs/{id}` | Single job |
| POST | `/api/designs/generate` | multipart generate |
