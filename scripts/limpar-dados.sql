-- Limpa os dados transacionais da API sem remover estrutura, migrations ou historico do EF.
-- Banco alvo: analise_planos_saude
-- Execute somente em ambiente local/homologacao.

SET FOREIGN_KEY_CHECKS = 0;

TRUNCATE TABLE `SimulacoesAnalises`;
TRUNCATE TABLE `SimulacoesPrestadoresVersoes`;
TRUNCATE TABLE `SimulacoesValoresFaixaVersoes`;
TRUNCATE TABLE `SimulacoesPlanosVersoes`;
TRUNCATE TABLE `SimulacoesColetasVersoes`;
TRUNCATE TABLE `SimulacoesAtualizacoesJobs`;
TRUNCATE TABLE `SimulacoesPrestadores`;
TRUNCATE TABLE `SimulacoesValoresFaixa`;
TRUNCATE TABLE `SimulacoesJobs`;
TRUNCATE TABLE `SimulacoesPlanos`;
TRUNCATE TABLE `SimulacoesColetas`;

TRUNCATE TABLE `AnaliseLinks`;
TRUNCATE TABLE `Analises`;

SET FOREIGN_KEY_CHECKS = 1;
