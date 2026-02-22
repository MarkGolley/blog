# deploy.ps1
$project = "my-blog-website-470819"
$image = "gcr.io/$project/myblog-app:latest"
$region = "europe-west2"

# Build Docker image
cd C:\Users\markg\RiderProjects\MyBlog
docker build -t $image .

# Push Docker image to GCR
docker push $image

# Deploy to Cloud Run
gcloud run deploy myblog-app `
  --image $image `
  --region $region `
  --platform managed `
  --allow-unauthenticated `
  --memory=256Mi `
  --cpu=0.25 `
  --concurrency=1 `
  --min-instances=0 `
  --max-instances=5 `
  --add-cloudsql-instances "my-blog-website-470819:europe-west2:myblog-db" `
  --timeout 10m
