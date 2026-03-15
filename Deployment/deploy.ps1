# deploy.ps1
$project = "my-blog-website-470819"
$service = "myblog-app"
$region = "europe-west2"
$tag = (Get-Date -Format "yyyyMMdd-HHmmss")
$image = "gcr.io/$project/$service:$tag"

# Build Docker image
cd C:\Users\markg\RiderProjects\blog
docker build -t $image .

# Push Docker image to GCR
docker push $image

# Deploy to Cloud Run
gcloud run deploy $service `
  --image $image `
  --region $region `
  --platform managed `
  --allow-unauthenticated `
  --set-env-vars "APP_VERSION=$tag" `
  --memory=256Mi `
  --cpu=0.25 `
  --concurrency=1 `
  --min-instances=0 `
  --max-instances=1 `
  --timeout 10m

Write-Host "Deployed image: $image"
Write-Host "APP_VERSION set to: $tag"
