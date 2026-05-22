gcloud config get-value project

gcloud projects list

gcloud config set project egen-gcr

gcloud artifacts repositories list    

gcloud services enable artifactregistry.googleapis.com
gcloud services enable run.googleapis.com

PROJECT_ID="egen-gcr"
REPO_NAME="egen-cicd-net"
REGION="us-west1"
gcloud artifacts repositories create $REPO_NAME \
  --repository-format=docker \
  --location=$REGION \
  --description="Docker repo"

docker tag web.apphost:latest us-west1-docker.pkg.dev/egen-cicd-net/egen-cicd-net/web-apphost:v1
docker push us-west1-docker.pkg.dev/egen-cicd-net/egen-cicd-net/web-apphost:v1 

  https://web-apphost-603087230604.europe-west1.run.app

PROJECT_ID="egen-cicd-net"
REGION="us-west1"
REPO="egen-cicd-net"
IMAGE="web-apphost"
TAG="v1"
SERVICE="web-apphost"
IMAGE_URL="us-west1-docker.pkg.dev/${PROJECT_ID}/${REPO}/${IMAGE}:${TAG}"

gcloud run deploy "${SERVICE}" \
  --image="${IMAGE_URL}" \
  --region="${REGION}" \
  --project="${PROJECT_ID}" \
  --platform=managed \
  --allow-unauthenticated \
  --port=8080 \
  --memory=512Mi \
  --cpu=1 \
  --min-instances=0 \
  --max-instances=1

$PROJECT_ID="egen-cicd"
$REGION="us-west1"
$REPO="egen-cicd-net"
$IMAGE="web-apphost"
$TAG="v1"
$SERVICE="web-apphost"
$IMAGE_URL="us-west1-docker.pkg.dev/$PROJECT_ID/$REPO/${IMAGE}:$TAG"

gcloud run deploy "$SERVICE" --image="$IMAGE_URL" --region="$REGION" --project="$PROJECT_ID" `
  --platform=managed --allow-unauthenticated --port=8080 --memory=512Mi --cpu=1 `
  --min-instances=0 --max-instances=1
  
gcloud run services list --region=$REGION --project=$PROJECT_ID

gcloud run services delete $SERVICE --region=$REGION --project=$PROJECT_ID --quiet


# configure for GAR push
JENKINS_GAR_PUSHER="jenkins-gar-pusher"
gcloud iam service-accounts create "$JENKINS_GAR_PUSHER" \
    --display-name="Jenkins GAR Pusher"

gcloud projects add-iam-policy-binding $PROJECT_ID \
    --member="serviceAccount:$JENKINS_GAR_PUSHER@$PROJECT_ID.iam.gserviceaccount.com" \
    --role="roles/artifactregistry.writer"

gcloud iam service-accounts keys create key.json \
    --iam-account=$JENKINS_GAR_PUSHER@$PROJECT_ID.iam.gserviceaccount.com


#configure GCR for pull/run
GCR_SA="jenkins-gcr-runner"
gcloud iam service-accounts create $GCR_SA \
    --display-name="MyApp Cloud Run runtime" \
    --project=$PROJECT_ID

GCR_SA_EMAIL="$GCR_SA@$PROJECT_ID.iam.gserviceaccount.com"
gcloud projects add-iam-policy-binding $PROJECT_ID \
  --member="serviceAccount:$GCR_SA_EMAIL" \
  --role="roles/artifactregistry.reader"

gcloud projects add-iam-policy-binding ${PROJECT_ID} \
    --member="serviceAccount:$GCR_SA_EMAIL" \
    --role="roles/run.admin"

gcloud iam service-accounts add-iam-policy-binding \
    myapp-runtime@${PROJECT_ID}.iam.gserviceaccount.com \
    --member="serviceAccount:${JENKINS_SA}" \
    --role="roles/iam.serviceAccountUser"

gcloud iam service-accounts keys create gcrkey.json \
  --iam-account=$GCR_SA@$PROJECT_ID.iam.gserviceaccount.com

gcloud iam service-accounts delete $GCR_SA_EMAIL --quiet


DEPLOYER_SA="jenkins-deployer@$PROJECT_ID.iam.gserviceaccount.com"
RUNTIME_SA="webapphost-runtime@egen-gcr.iam.gserviceaccount.com"
gcloud iam service-accounts get-iam-policy $RUNTIME_SA --format=json

