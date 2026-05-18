pipeline {
    parameters {
        string(name: 'BUILD_CONTAINER_IMAGE', defaultValue: 'mcr.microsoft.com/dotnet/sdk:10.0', description: 'Image for the build container')
        string(name: 'BUILD_CONTAINER_ARGS', defaultValue: '-v /tmp/nuget:/tmp/nuget -e DOTNET_CLI_TELEMETRY_OPTOUT=1 --net=cicd-net', description: 'Arguments for the build container')
        string(name: 'BUILD_FILE', defaultValue: 'cicd.sln', description: 'File to build (sln or csproj)')
        string(name: 'PACK_VER', defaultValue: '0.0.1', description: 'Explicit version for package')
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
    }
}
