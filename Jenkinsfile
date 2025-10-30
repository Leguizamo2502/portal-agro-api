pipeline {
    agent any

    environment {
        DOTNET_CLI_HOME = '/var/jenkins_home/.dotnet'
        DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
        DOTNET_NOLOGO = '1'

        // Directorios y rutas
        APP_DIR       = 'Portal-Agro-comercial-del-Huila'
        PROJECT_PATH  = 'Portal-Agro-comercial-del-Huila/Web/Web.csproj'
        DB_COMPOSE_FILE = 'portal-agro-db/docker-compose.yml'   // compose de BDs compartidas
        DOCKER_NETWORK = 'portal_agro_network'
    }

    stages {
        // 1) Checkout
        stage('Checkout código fuente') {
            steps {
                echo 'Clonando repositorio desde GitHub...'
                checkout scm
                sh 'ls -R Portal-Agro-comercial-del-Huila/DevOps || true'
                sh 'ls -R portal-agro-db || true'
            }
        }

        // 2) Detectar entorno según rama
        stage('Detectar entorno') {
            steps {
                script {
                    switch (env.BRANCH_NAME) {
                        case 'main':    env.ENVIRONMENT = 'prod';     break
                        case 'staging': env.ENVIRONMENT = 'staging';  break
                        case 'qa':      env.ENVIRONMENT = 'qa';       break
                        default:        env.ENVIRONMENT = 'develop';  break
                    }

                    env.ENV_DIR      = "${APP_DIR}/DevOps/${env.ENVIRONMENT}"
                    env.COMPOSE_FILE = "${env.ENV_DIR}/docker-compose.yml"
                    env.ENV_FILE     = "${env.ENV_DIR}/.env"

                    echo """
                    Rama: ${env.BRANCH_NAME}
                    Entorno: ${env.ENVIRONMENT}
                    Compose API: ${env.COMPOSE_FILE}
                    Env API: ${env.ENV_FILE}
                    Compose DB: ${env.DB_COMPOSE_FILE}
                    """

                    if (!fileExists(env.COMPOSE_FILE)) { error "No se encontró ${env.COMPOSE_FILE}" }
                    if (!fileExists(env.DB_COMPOSE_FILE)) { echo "Aviso: no se encontró ${env.DB_COMPOSE_FILE} (se omitirá stack de BDs)" }
                }
            }
        }

        // 3) Compilar y publicar .NET dentro de contenedor SDK
        stage('Compilar .NET dentro de contenedor SDK') {
            steps {
                script {
                    docker.image('mcr.microsoft.com/dotnet/sdk:9.0')
                          .inside('-v /var/run/docker.sock:/var/run/docker.sock -u root:root') {
                              sh """
                            echo 'Restaurando dependencias .NET...'
                            cd ${APP_DIR}
                            dotnet restore Web/Web.csproj
                            dotnet build   Web/Web.csproj --configuration Release
                            dotnet publish Web/Web.csproj -c Release -o ./publish
                        """
                          }
                }
            }
        }

        // 4) Construir imagen Docker de la API
        stage('Construir imagen Docker API') {
            steps {
                script {
                    sh """
                echo 'Construyendo imagen Docker para ${env.ENVIRONMENT}...'
                docker build \
                  -t portal-agro-api-${env.ENVIRONMENT}:latest \
                  -f Portal-Agro-comercial-del-Huila/Web/Dockerfile \
                  .
            """
                }
            }
        }

        // 5) Preparar red y levantar stack de BDs (independiente)
        stage('Preparar red y base de datos') {
            steps {
                script {
                    sh """
                        echo 'Creando red externa compartida (si no existe)...'
                        docker network create ${DOCKER_NETWORK} || true
                    """

                    if (fileExists(env.DB_COMPOSE_FILE)) {
                        sh """
                            echo 'Levantando stack de BDs...'
                            docker compose -f ${env.DB_COMPOSE_FILE} up -d
                        """
                    } else {
                        echo "Se omite el stack de BDs por falta de ${env.DB_COMPOSE_FILE}"
                    }
                }
            }
        }

        // 6) Desplegar API con docker compose del entorno
        stage('Desplegar API') {
            steps {
                script {
                    def composeCmd = "docker compose -f ${env.COMPOSE_FILE}"
                    if (fileExists(env.ENV_FILE)) {
                        composeCmd += " --env-file ${env.ENV_FILE}"
                    } else {
                        echo "Aviso: no existe ${env.ENV_FILE}; el compose usará valores por defecto/variables del sistema."
                    }
                    composeCmd += ' up -d --build'
                    sh composeCmd
                }
            }
        }
    }

    post {
        success { echo "Despliegue completado correctamente para ${env.ENVIRONMENT}" }
        failure { echo "Error durante el despliegue en ${env.ENVIRONMENT}" }
        always  { echo 'Limpieza final del pipeline completada.' }
    }
}
