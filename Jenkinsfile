pipeline {
    agent any

    environment {
        DOTNET_CLI_HOME = '/var/jenkins_home/.dotnet'
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        DOTNET_NOLOGO = '1'
        PROJECT_PATH = 'Portal-Agro-comercial-del-Huila/Web/Web.csproj'
    }

    stages {
        stage('Checkout código fuente') {
            steps {
                echo '📥 Clonando repositorio desde GitHub...'
                checkout scm
                sh 'ls -R Portal-Agro-comercial-del-Huila/DevOps || true'
            }
        }

        stage('Detectar entorno') {
            steps {
                script {
                    switch (env.BRANCH_NAME) {
                        case 'main':
                            env.ENVIRONMENT = 'prod'
                            break
                        case 'staging':
                            env.ENVIRONMENT = 'staging'
                            break
                        case 'qa':
                            env.ENVIRONMENT = 'qa'
                            break
                        default:
                            env.ENVIRONMENT = 'develop'
                            break
                    }

                    env.ENV_DIR      = "Portal-Agro-comercial-del-Huila/DevOps/${env.ENVIRONMENT}"
                    env.COMPOSE_FILE = "${env.ENV_DIR}/docker-compose.yml"
                    env.ENV_FILE     = "${env.ENV_DIR}/.env"

                    echo """
                    ✅ Rama detectada: ${env.BRANCH_NAME}
                    🌎 Entorno asignado: ${env.ENVIRONMENT}
                    📄 Compose file: ${env.COMPOSE_FILE}
                    📁 Env file: ${env.ENV_FILE}
                    """

                    if (!fileExists(env.COMPOSE_FILE)) {
                        error "❌ No se encontró ${env.COMPOSE_FILE}"
                    }
                }
            }
        }

        stage('Compilar .NET dentro de contenedor SDK') {
            steps {
                script {
                    docker.image('mcr.microsoft.com/dotnet/sdk:9.0')
                        .inside('-v /var/run/docker.sock:/var/run/docker.sock -u root:root') {
                            sh '''
                            echo "🔧 Restaurando dependencias .NET..."
                            cd Portal-Agro-comercial-del-Huila
                            dotnet restore Web/Web.csproj
                            dotnet build Web/Web.csproj --configuration Release
                            dotnet publish Web/Web.csproj -c Release -o ./publish
                        '''
                        }
                }
            }
        }

        // =============================
        // ESTE STAGE ES EL QUE CAMBIA
        // =============================
        stage('Construir imagen Docker') {
            steps {
                script {
                    sh """
                        echo "🐳 Construyendo imagen Docker para Portal-Agro-comercial-del-Huila (${env.ENVIRONMENT})"
                        docker build \
                            -t portal-agro-api-${env.ENVIRONMENT}:latest \
                            -f Portal-Agro-comercial-del-Huila/Web/Dockerfile \
                            .
                    """
                }
            }
        }

        stage('Desplegar Portal-Agro-comercial-del-Huila') {
            steps {
                script {
                    sh """
                    echo "🚀 Desplegando entorno: ${env.ENVIRONMENT}"
                """

                    def composeCmd = "docker compose -f ${env.COMPOSE_FILE}"
                    if (fileExists(env.ENV_FILE)) {
                        composeCmd += " --env-file ${env.ENV_FILE}"
                } else {
                        echo "⚠ No se encontró ${env.ENV_FILE}, se continúa sin --env-file"
                    }

                    composeCmd += ' up -d'

                    sh composeCmd
                }
            }
        }
    }

    post {
        success {
            echo "🎉 Despliegue completado correctamente para ${env.ENVIRONMENT}"
        }
        failure {
            echo "💥 Error durante el despliegue en ${env.ENVIRONMENT}"
        }
        always {
            echo '🧹 Limpieza final del pipeline completada.'
        }
    }
}
