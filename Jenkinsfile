pipeline {
    parameters {
        string(name: 'BUILD_CONTAINER_IMAGE', defaultValue: 'netsdk10:latest', description: 'Image for the build container')
        string(name: 'BUILD_CONTAINER_ARGS', defaultValue: '-v /tmp/nuget:/tmp/nuget -e DOTNET_CLI_TELEMETRY_OPTOUT=1 --net=cicd-net -u root -v /var/run/docker.sock:/var/run/docker.sock --group-add 0', description: 'Arguments for the build container')
        string(name: 'BUILD_FILE', defaultValue: 'cicd.sln', description: 'File to build (sln or csproj)')
        string(name: 'PACK_VER', defaultValue: '0.0.1', description: 'Explicit version for package')
        string(name: 'WEBAPPHOST_CONTAINER_NAME', defaultValue: 'webapphost-njg', description: 'Name for the local web app host container')
        string(name: 'GCP_REGION', defaultValue: 'us-west1', description: 'GCP region for artifact registry')
        string(name: 'GAR_REGION', defaultValue: 'us-west1', description: 'GAR region for artifact registry')
        string(name: 'GAR_REPOSITORY_NAME', defaultValue: 'egen-cicd-net', description: 'GCP artifact repository name')
        string(name: 'GAR_APPHOST_CONTAINER_NAME', defaultValue: 'web-apphost', description: 'Name for the web app host container in GAR')
        string(name: 'GCP_PROJECT_ID', defaultValue: 'egen-gcr', description: 'GCP project ID')
        string(name: 'GAR_SERVICE_ACCOUNT_ID', defaultValue: 'gar-service-account', description: 'Gar service account name')
        string(name: 'GCR_APPHOST_SERVICE', defaultValue: 'webapphost', description: 'GCR web app host service name')
        string(name: 'GAR_APPHOST_VERSION', defaultValue: 'v1', description: 'GAR app host version')
        string(name: 'GCR_REGION', defaultValue: 'us-west1', description: 'GCR region')
        string(name: 'GCR_SERVICE_ACCOUNT_ID', defaultValue: 'gcr-service-account', description: 'GCR service account name')
        string(name: 'GCR_WEBAPPHOST_RUNTIME_SA', defaultValue: 'webapphost-runtime', description: 'GCR web app host runtime service account name')
    }
    agent {
        docker {
            image "${params.BUILD_CONTAINER_IMAGE}"
            args "${BUILD_CONTAINER_ARGS}" 
            reuseNode true
        }
    }
    stages {
        stage('Init') {
            steps {
                script {
                    println "Initializing build for ${params.BUILD_FILE}"
                    println "Using build container image: ${params.BUILD_CONTAINER_IMAGE}"
                    println "Build container arguments: ${params.BUILD_CONTAINER_ARGS}"
                    println "Build #${params.BUILD_NUMBER} started at ${new Date().format("yyyy-MM-dd HH:mm:ss")}"
                }
            }
        }
        // stage('Clone') {
        //     steps { 
        //         sh 'rm -rf $"{params.REPO_DIR}"'

        //         git url: "${params.GIT_URL}",
        //             branch: "${params.GIT_BRANCH}"
        //     }
        // }
        // stage('Restore') {
        //     steps { 
        //         sh "dotnet restore ${params.BUILD_FILE}"
        //     }
        // }
        // stage('Build') {
        //     steps { 
        //         sh "dotnet build --no-restore ${params.BUILD_FILE}"
        //     }
        // }
        // // stage('Pack') {
        // //     steps{
        // //         print("${params.PACK_VER}")
        // //         sh 'dotnet pack /p:Version=${params.PACK_VER} -c Release /p: ./src/YourOrg.Common.Core/YourOrg.Common.Core.csproj'
        // //         sh "ls -ls ./src/YourOrg.Common.Core/bin/Release"
        // //     }
        // // }

        // stage('Create local docker image') {
        //     steps {
        //         sh "docker build --file ./src/Web.AppHost/Dockerfile  -t webapphost-njg:latest ."
        //     }
        // }

        // stage('Tag local docker image for GAR') {
        //     steps {
        //         sh "docker tag ${params.WEBAPPHOST_CONTAINER_NAME}:latest ${params.GCP_REGION}-docker.pkg.dev/${params.GCP_PROJECT_ID}/${params.GAR_REPOSITORY_NAME}/${params.GAR_APPHOST_CONTAINER_NAME}:v1"
        //     }
        // }

        // stage('Push to GAR') {
        //     steps {
        //         withCredentials([file(credentialsId: "${params.GAR_SERVICE_ACCOUNT_ID}", variable: 'GOOGLE_APPLICATION_CREDENTIALS')]) {
        //             sh """
        //                 cat "$GOOGLE_APPLICATION_CREDENTIALS" | docker login -u _json_key --password-stdin https://${params.GAR_REGION}-docker.pkg.dev
        //                 docker push "${params.GAR_REGION}-docker.pkg.dev/${params.GCP_PROJECT_ID}/${params.GAR_REPOSITORY_NAME}/${params.GAR_APPHOST_CONTAINER_NAME}:v1"
        //             """
        //         }
        //     }
        // }

        // stage('Install gcloud') {
        //     steps {
        //         sh '''
        //             apt-get update
        //             apt-get install -y apt-transport-https ca-certificates gnupg curl
        //             echo "deb [signed-by=/usr/share/keyrings/cloud.google.gpg] https://packages.cloud.google.com/apt cloud-sdk main" \
        //                 | tee /etc/apt/sources.list.d/google-cloud-sdk.list
        //             curl https://packages.cloud.google.com/apt/doc/apt-key.gpg \
        //                 | gpg --dearmor -o /usr/share/keyrings/cloud.google.gpg
        //             apt-get update
        //             apt-get install -y google-cloud-cli
        //         '''
        //     }
        // }

        stage('Authenticate to GCP') {
            steps {
                withCredentials([file(credentialsId: 'gar-service-account', variable: 'GCP_KEY_FILE')]) {
                    sh """
                        gcloud auth activate-service-account --key-file="$GCP_KEY_FILE"
                        gcloud config set project \${params.GCP_PROJECT_ID}
                        gcloud auth configure-docker \${params.GCP_REGION}-docker.pkg.dev --quiet
                        gcloud auth list
                        echo "Authentication successful, ready to push to GAR"
                    """
                }
            }
        }

        // stage('Deploy to Cloud Run') {
        //     steps {
        //         script {
        //             def runtimeSA = "${params.GCR_WEBAPPHOST_RUNTIME_SA}@${params.GCP_PROJECT_ID}.iam.gserviceaccount.com"
        //             def image = "${params.GAR_REGION}-docker.pkg.dev/${params.GCP_PROJECT_ID}/${params.GAR_REPOSITORY_NAME}/${params.GAR_APPHOST_CONTAINER_NAME}:${params.GAR_APPHOST_VERSION}"
        //             println "${image}"
        //             def cmd = "gcloud run deploy ${params.GCR_APPHOST_SERVICE} --image=${image} --project=${params.GCP_PROJECT_ID} --region=${params.GCR_REGION} --service-account=${runtimeSA} --platform=managed --allow-unauthenticated --port=8080 --memory=512Mi --cpu=1 --min-instances=0 --max-instances=1"
        //             println "${cmd}"
        //             sh "${cmd}"
        //         }
        //     }
        // }

        stage('Verify Deployment') {
            steps {
                script {
                    sh """
                        SERVICE_URL=\$(gcloud run services describe ${params.GCR_APPHOST_SERVICE} --region=${params.GCR_REGION} --project=${params.GCP_PROJECT_ID} --format='value(status.url)')
                        echo "Deployed to: \$SERVICE_URL"
                    """
                }
            }
        }
    }

    post {
        always {
            sh """
                gcloud auth revoke --all || true
                docker logout \${params.GCP_REGION}-docker.pkg.dev || true
            """
        }
        success {
            echo "Build #\${BUILD_NUMBER} deployed successfully as \${params.GAR_APPHOST_CONTAINER_NAME}:\${params.GAR_APPHOST_VERSION}"
        }
        failure {
            echo "Build #\${BUILD_NUMBER} failed at stage: ${env.STAGE_NAME}"
        }
    }
}
