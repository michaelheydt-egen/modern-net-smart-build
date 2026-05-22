def GCP_AUTHENTICATED = false
def NEXUS_DOCKER_AUTHENTICATED = false
def CONTAINER_TAG = "latest"

pipeline {
    parameters {
        string(name: 'BUILD_CONTAINER_IMAGE', defaultValue: 'netsdk10:latest', description: 'Image for the build container')
        string(name: 'BUILD_CONTAINER_ARGS', defaultValue: '-v /tmp/nuget:/tmp/nuget -e DOTNET_CLI_TELEMETRY_OPTOUT=1 --net=cicd-net -u root -v /var/run/docker.sock:/var/run/docker.sock --group-add 0', description: 'Arguments for the build container')
        string(name: 'BUILD_FILE', defaultValue: 'cicd.sln', description: 'File to build (sln or csproj)')
        string(name: 'DOCKER_BUILD_FILE', defaultValue: './src/Web.AppHost/Dockerfile', description: 'Path to the Dockerfile for building the web app host image')
        string(name: 'PACK_VER', defaultValue: '1.0.0', description: 'Explicit version for package')
        string(name: 'CONTAINER_NAME', defaultValue: 'webapphost', description: 'Name for the local container')
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
        string(name: 'NUGET_SOURCE', defaultValue: 'http://nexus:8081/repository/nuget-hosted/', description: 'NuGet feed URL (Nexus hosted repo, internal proxy, or https://api.nuget.org/v3/index.json)')
        string(name: 'NUGET_API_KEY_CREDENTIAL_ID', defaultValue: 'rhythm-nuget', description: 'Jenkins credential id (Secret Text) holding the NuGet API key for the target feed')
        string(name: 'NEXUS_DOCKER_HOST', defaultValue: 'nexus:8081', description: 'Nexus docker registry host:port (port determines the target hosted repo via the Nexus connector)')
        string(name: 'NEXUS_DOCKER_CREDENTIAL_ID', defaultValue: 'rhythm-docker', description: 'Jenkins credential id (Username/Password) for the Nexus docker registry')
        string(name: 'NEXUS_DOCKER_USER', defaultValue: 'admin', description: 'Nexus docker registray username (for Jenkins credentials)')
        string(name: 'NEXUS_DOCKER_PROTOCOL', defaultValue: 'http://', description: 'Nexus communications protocol (http or https')
    }
    
    options {
        timestamps()
        timeout(time: 30, unit: 'MINUTES')
        
        disableConcurrentBuilds()
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
                    sh "git config --global --add safe.directory '*'"

                    println "Initializing build for ${params.BUILD_FILE}"
                    println "Using build container image: ${params.BUILD_CONTAINER_IMAGE}"
                    println "Build container arguments: ${params.BUILD_CONTAINER_ARGS}"
                    println "Build #${BUILD_NUMBER} started at ${new Date().format("yyyy-MM-dd HH:mm:ss")}"

                    GIT_COMMIT_HASH = sh (script: "git log -n 1 --pretty=format:'%H'", returnStdout: true)
                    println "Git commit hash: ${GIT_COMMIT_HASH}"
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

        stage('Restore') {
            steps { 
                sh "dotnet restore ${params.BUILD_FILE}"
            }
        }

        stage('Build') {
            steps { 
                sh "dotnet build --no-restore -c Release ${params.BUILD_FILE}"
            }
        }

        stage ('Test') {
            steps { 
                //  sh "dotnet test --no-restore --no-build -c Release ${params.BUILD_FILE} --logger trx"
                echo "Skipping tests for now"
            }
        }

        stage('Pack') {
            steps{
                print("${params.PACK_VER}")
                sh "dotnet pack /p:Version=${params.PACK_VER} -c Release /p: PackageOutputPath=${env.WORKSPACE}/nupkgs ${params.BUILD_FILE}"
                sh "ls -lsR ${env.WORKSPACE}/nupkgs"            
            }
        }

        // stage('Publish Artifacts') {
        //     steps {
        //         withCredentials([string(credentialsId: "${params.NUGET_API_KEY_CREDENTIAL_ID}", variable: 'NUGET_API_KEY')]) {
        //             sh """
        //                 set -e
        //                 pkg_dir="${env.WORKSPACE}/nupkgs"
        //                 ls -lsR "\$pkg_dir"
        //                 echo "Publishing NuGet packages from \$pkg_dir to ${params.NUGET_SOURCE}"
        //                 if ! ls "\$pkg_dir"/*.nupkg >/dev/null 2>&1; then
        //                     echo "ERROR: No .nupkg files found in \$pkg_dir"
        //                     exit 1
        //                 fi
        //                 for pkg in "\$pkg_dir"/*.nupkg; do
        //                     echo "Pushing \$pkg"
        //                     dotnet nuget push "\$pkg" \\
        //                         --source "${params.NUGET_SOURCE}" \\
        //                         --api-key "\$NUGET_API_KEY" \\
        //                         --skip-duplicate \\
        //                         --allow-insecure-connections 
        //                 done
        //             """
        //         }
        //     }
        // }

        stage('Create local docker image') {
            steps {
                sh "docker build --file ${params.DOCKER_BUILD_FILE} -t ${params.CONTAINER_NAME}:${CONTAINER_TAG} ."
            }
        }

        stage('Push to Nexus') {
            steps {
                withCredentials([usernamePassword(credentialsId: "${params.NEXUS_DOCKER_CREDENTIAL_ID}", usernameVariable: 'NEXUS_DOCKER_USER', passwordVariable: 'NEXUS_DOCKER_PASS')]) {
                    script {
                        sh """
                            set -e
                            echo "Tagging local image ${params.CONTAINER_NAME}:${CONTAINER_TAG} for Nexus registry ${params.NEXUS_DOCKER_HOST}"
                            docker tag ${params.CONTAINER_NAME}:${CONTAINER_TAG} ${params.NEXUS_DOCKER_HOST}/${params.CONTAINER_NAME}:${CONTAINER_TAG}

                            echo "Logging in to Nexus docker registry ${params.NEXUS_DOCKER_HOST}"
                            echo "\$NEXUS_DOCKER_PASS" | docker login ${NEXUS_DOCKER_PROTOCOL}${params.NEXUS_DOCKER_HOST} -u "\$NEXUS_DOCKER_USER" --password-stdin

                            echo "Pushing image to Nexus registry ${params.NEXUS_DOCKER_HOST}"
                            docker push ${params.NEXUS_DOCKER_HOST}/${params.CONTAINER_NAME}:${CONTAINER_TAG}
                        """
                        NEXUS_DOCKER_AUTHENTICATED = true
                    }
                }
            }
        }

        // stage('Tag local docker image for GAR') {
        //     steps {
        //         sh "docker tag ${params.CONTAINER_NAME}:${CONTAINER_TAG} ${params.GCP_REGION}-docker.pkg.dev/${params.GCP_PROJECT_ID}/${params.GAR_REPOSITORY_NAME}/${params.GAR_APPHOST_CONTAINER_NAME}:v1"
        //     }
        // }

        // stage('Authenticate to GCP') {
        //     steps {
        //         withCredentials([file(credentialsId: 'gar-service-account', variable: 'GCP_KEY_FILE')]) {
        //             script {
        //                 sh """
        //                     echo "Authenticating to GCP with service account key from Jenkins credentials"

        //                     gcloud auth activate-service-account --key-file="$GCP_KEY_FILE"
        //                     gcloud config set project ${params.GCP_PROJECT_ID}
        //                     gcloud auth configure-docker ${params.GCP_REGION}-docker.pkg.dev --quiet
        //                     gcloud auth list

        //                     echo "Authentication successful, ready to push to GAR"
        //                 """

        //                 GCP_AUTHENTICATED = true
        //             }
        //         }
        //     }
        // }

        // stage('Push to GAR') {
        //     steps {
        //         script {
        //             sh """
        //                 # docker login -u _json_key --password-stdin https://${params.GAR_REGION}-docker.pkg.dev
        //                 docker push "${params.GAR_REGION}-docker.pkg.dev/${params.GCP_PROJECT_ID}/${params.GAR_REPOSITORY_NAME}/${params.GAR_APPHOST_CONTAINER_NAME}:v1"
        //             """
        //         }
        //     }
        // }

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

        // stage('Verify Deployment') {
        //     steps {
        //         script {
        //             sh """
        //                 SERVICE_URL=\$(gcloud run services describe ${params.GCR_APPHOST_SERVICE} --region=${params.GCR_REGION} --project=${params.GCP_PROJECT_ID} --format='value(status.url)')
        //                 echo "Deployed to: \$SERVICE_URL"
        //             """
        //         }
        //     }
        // }

        // stage('Health Check') {
        //     steps {
        //         script {
        //             sh """
        //                 HEALTH_CHECK_URL=\$(gcloud run services describe ${params.GCR_APPHOST_SERVICE} --region=${params.GCR_REGION} --project=${params.GCP_PROJECT_ID} --format='value(status.url)')/alive
        //                 echo \$HEALTH_CHECK_URL
        //                 curl -fsSL --max-time 30 \$HEALTH_CHECK_URL || echo "Health check failed"
        //             """
        //         }
        //     }
        // }
    }

    post {
        always {
            script {
                if (GCP_AUTHENTICATED) {
                    println "Cleaning up GCP authentication"
                    sh "gcloud auth revoke --all || true"
                    sh "docker logout ${params.GCP_REGION}-docker.pkg.dev || true"
                }
            }
            echo "Post-build cleanup completed"
        }
        success {
            echo "Build #${BUILD_NUMBER} deployed successfully as ${params.GAR_APPHOST_CONTAINER_NAME}:${params.GAR_APPHOST_VERSION}"
        }
        failure {
            echo "Build #${BUILD_NUMBER} failed at stage: ${env.STAGE_NAME}"
        }
    }
}
