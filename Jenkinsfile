pipeline {
    parameters {
        string(name: 'BUILD_CONTAINER_IMAGE', defaultValue: 'mcr.microsoft.com/dotnet/sdk:10.0', description: 'Image for the build container')
        string(name: 'BUILD_CONTAINER_ARGS', defaultValue: '-v /tmp/nuget:/tmp/nuget -e DOTNET_CLI_TELEMETRY_OPTOUT=1 --net=cicd-net', description: 'Arguments for the build container')
        string(name: 'BUILD_FILE', defaultValue: 'cicd.sln', description: 'File to build (sln or csproj)')
        string(name: 'PACK_VER', defaultValue: '0.0.1', description: 'Explicit version for package')
        string(name: 'WEBAPPHOST_CONTAINER_NAME', defaultValue: 'webapphost-njg', description: 'Name for the local web app host container')
        string(name: 'GCP_REGION', defaultValue: 'us-west1', description: 'GCP region for artifact registry')
        string(name: 'GCP_REPOSITORY_NAME', defaultValue: 'egen-cicd-net', description: 'GCP artifact repository name')
    }
    agent {
        docker {
            image "${params.BUILD_CONTAINER_IMAGE}"
            args "${BUILD_CONTAINER_ARGS}"
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
        stage('Restore') {
            steps { 
                sh "dotnet restore ${params.BUILD_FILE}"
            }
        }
        stage('Build') {
            steps { 
                sh "dotnet build --no-restore ${params.BUILD_FILE}"
            }
        }
        // stage('Pack') {
        //     steps{
        //         print("${params.PACK_VER}")
        //         sh 'dotnet pack /p:Version=${params.PACK_VER} -c Release /p: ./src/YourOrg.Common.Core/YourOrg.Common.Core.csproj'
        //         sh "ls -ls ./src/YourOrg.Common.Core/bin/Release"
        //     }
        // }

        stage('Create local docker image') {
            steps {
                sh "docker build --file ./src/Web.AppHost/Dockerfile  -t webapphost-njg:latest ."
            }
        }

        stage('Tag local docker image for GAR') {
            steps {
                sh "docker tag ${params.WEBAPPHOST_CONTAINER_NAME}:latest ${params.GCP_REGION}-docker.pkg.dev/${params.GCP_REPOSITORY_NAME}/${params.GCP_REPOSITORY_NAME}:v1"
            }
        }
    }
}
