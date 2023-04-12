import { Button, Card, Col, Row, message } from 'antd';
import Meta from 'antd/es/card/Meta';
import Team from './Team';
import { useContext } from 'react';
import { TeamsContext } from '../Store/TeamsContext';
import { getResultString } from '../Adapters/DB/WebApiAdapter';
import { getTextResult } from '../Utilities/Helpers'
import { ConfigurationContext } from '../Store/ConfigurationContext';
import { WhatsappShareButton, TwitterIcon, WhatsappIcon } from "react-share";
import { CopyOutlined } from '@ant-design/icons';


function Teams(props) {
    const [messageApi, contextHolder] = message.useMessage();

    const teamsContext = useContext(TeamsContext);
    const configContext = useContext(ConfigurationContext)

    const team1 = `${teamsContext.teams[0].teamSymbol} Team ${teamsContext.teams[0].teamName}`
    const team2 = `${teamsContext.teams[1].teamSymbol} Team ${teamsContext.teams[1].teamName}`
    const team3 = `${teamsContext.teams[2].teamSymbol} Team ${teamsContext.teams[2].teamName}`


    async function copyToClipboard() {
        const resultString = getTextResult(teamsContext.teams, configContext.config.config.eventDate)
        navigator.clipboard.writeText(resultString)

        messageApi.open({
            type: 'success',
            content: 'Copied!',
          });
    }

    return (
        <div>
            {contextHolder}

            <Row gutter={1}>
                <Col span={8}>
                    <Card title={team1} bordered={false} >
                        <Team team={teamsContext.teams[0]} />


                    </Card>
                </Col>
                <Col span={8}>
                    <Card title={team2} bordered={false}>
                        <div>
                            <Team team={teamsContext.teams[1]} />
                        </div>
                    </Card>
                </Col>
                <Col span={8}>
                    <Card title={team3} bordered={false}>
                        <Team team={teamsContext.teams[2]} />



                    </Card>
                </Col>

            </Row>


            <div style={{ background: 'white', display: 'flex',  'flex-direction': 'row', 'align-items': 'center', 'justifyContent': 'flex-end' }}>

                <div style={{ background: 'white', display: 'flex',  'flex-direction': 'row', 'align-items': 'center', 'justifyContent': 'flex-end' }}>
                    <Button icon={<CopyOutlined />} onClick={copyToClipboard} style={{ borderRadius: '5px', marginTop: '5px', marginRight: '5px' }} />


                    <WhatsappShareButton url={getTextResult(teamsContext.teams, configContext.selectedEventDate)}>
                        <WhatsappIcon style={{ borderRadius: '5px', marginBottom: '-10px', marginRight: '5px' }} size={32} />
                    </WhatsappShareButton >
                </div>


                <div>
                    <Button onClick={() => { props.onShiffleClicked(configContext.algo) }} type="primary" style={{ borderRadius: '5px', marginTop: '5px', marginRight: '5px' }}>
                        Run Again
                    </Button>
                </div>

            </div>
        </div>
    );

}



export default Teams;