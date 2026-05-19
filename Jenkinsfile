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
        string(name: 'GCR_APPHOST_VERSION', defaultValue: 'v1', description: 'GAR app host version')
        string(name: 'GCR_REGION', defaultValue: 'us-west1', description: 'GCR region')
    }
    agent {
        docker {
            image "${params.BUILD_CONTAINER_IMAGE}"
            args "${BUILD_CONTAINER_ARGS}" 
            reuseNode true
        }
    }
    stages {
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

        stage('Deploy to Cloud Run') {
            steps {
                sh """
                    IMAGE="${params.GAR_REGION}-docker.pkg.dev/${params.GCP_PROJECT_ID}/${params.GAR_REPOSITORY_NAME}/${params.GAR_APPHOST_CONTAINER_NAME}:${params.GAR_APPHOST_VERSION}"
                    echo "$IMAGE"
                    gcloud run deploy ${params.GCR_APPHOST_SERVICE} --image=${IMAGE} --region=${params.GCR_REGION} --platform=managed --allow-unauthenticated --port=8080 --memory=512Mi --cpu=1 --min-instances=0 --max-instances=1
                """
            }
        }
    }
}
