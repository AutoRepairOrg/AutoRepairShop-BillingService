# language: pt
Funcionalidade: Orçamento da saga
  O Billing cria o orçamento quando a ordem chega para aprovação e publica a decisão do cliente.

  Cenário: Cliente aprova o orçamento
    Dado a ordem "11111111-1111-1111-1111-111111111111" enviada para aprovação com o item "Troca de óleo" de 150 para "João Silva" e o veículo "ABC1D23"
    Então o orçamento está "Draft"
    Quando o orçamento é enviado ao cliente
    E o cliente aprova o orçamento
    Então o orçamento está "Approved"
    E o evento "BudgetApprovedEvent" foi publicado

  Cenário: Cliente recusa o orçamento
    Dado a ordem "22222222-2222-2222-2222-222222222222" enviada para aprovação com o item "Troca de óleo" de 150 para "João Silva" e o veículo "ABC1D23"
    Quando o orçamento é enviado ao cliente
    E o cliente recusa o orçamento pelo motivo "Preço alto"
    Então o orçamento está "Rejected"
    E o evento "BudgetRejectedEvent" foi publicado

  Cenário: Cancelamento da ordem desfaz o orçamento
    Dado a ordem "33333333-3333-3333-3333-333333333333" enviada para aprovação com o item "Troca de óleo" de 150 para "João Silva" e o veículo "ABC1D23"
    Quando a ordem é cancelada pelo motivo "Cliente desistiu"
    Então o orçamento está "Cancelled"
