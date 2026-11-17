# The base image comes from infra.json (passed as a build arg by AppHost and the integration tests), so the broker
# version stays pinned in one place. MassTransit's UseDelayedMessageScheduler needs the delayed-message exchange,
# which the official image does not ship.
ARG RABBITMQ_IMAGE=rabbitmq:4.0-management-alpine
FROM ${RABBITMQ_IMAGE}

# The plugin is built per RabbitMQ minor version: bump version and checksum together with the base image.
ARG DELAYED_EXCHANGE_VERSION=4.0.7
ARG DELAYED_EXCHANGE_SHA256=9f746962d8f4e9ec2ce52fc86856859c30ed11abc67dd93cd80ebb3ef925d3fd

RUN set -eux; \
    plugin="/opt/rabbitmq/plugins/rabbitmq_delayed_message_exchange-${DELAYED_EXCHANGE_VERSION}.ez"; \
    wget -q -O "$plugin" "https://github.com/rabbitmq/rabbitmq-delayed-message-exchange/releases/download/v${DELAYED_EXCHANGE_VERSION}/rabbitmq_delayed_message_exchange-v${DELAYED_EXCHANGE_VERSION}.ez"; \
    echo "${DELAYED_EXCHANGE_SHA256}  $plugin" | sha256sum -c -; \
    chmod 644 "$plugin"; \
    rabbitmq-plugins enable --offline rabbitmq_delayed_message_exchange
